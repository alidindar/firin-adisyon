import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { ordersApi, type OrderItem, type TableDetail } from '../../api/orders';
import type { Product } from '../../api/types';
import { formatKurus } from '../../lib/format';
import { useOnline } from '../../realtime/RealtimeProvider';
import { GramKeypad } from './GramKeypad';
import { OrderPanel } from './OrderPanel';
import { ProductPicker } from './ProductPicker';

type GramTarget = { product: Product } | { item: OrderItem };

export function TableDetailPage() {
  const tableId = Number(useParams().id);
  const qc = useQueryClient();
  const online = useOnline();
  const table = useQuery({
    queryKey: ['table', tableId],
    queryFn: () => ordersApi.table(tableId),
    enabled: Number.isInteger(tableId),
  });
  const [gram, setGram] = useState<GramTarget | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<'order' | 'products'>('products');

  // Her işlem güncel masayı döndürür; bu cihaz beklemeden güncellenir, diğerleri SignalR ile.
  const change = useMutation({
    mutationFn: (run: () => Promise<TableDetail>) => run(),
    onSuccess: (detail) => {
      qc.setQueryData(['table', tableId], detail);
      qc.invalidateQueries({ queryKey: ['tables'] });
      setError(null);
      setGram(null);
    },
    onError: (e) => {
      setError(e.message);
      // Eski veriden gelen çakışmada (409) ekran güncel hâline dönsün.
      qc.invalidateQueries({ queryKey: ['table', tableId] });
    },
  });

  if (!Number.isInteger(tableId))
    return (
      <div className="page">
        <p className="error">Masa bulunamadı.</p>
        <Link to="/">← Masalara dön</Link>
      </div>
    );
  if (table.isPending) return <p>Yükleniyor…</p>;
  if (table.isError)
    return (
      <div className="page">
        <p className="error">{table.error.message}</p>
        <Link to="/">← Masalara dön</Link>
      </div>
    );

  const t = table.data;
  const disabled = !online;
  const pick = (p: Product) => {
    if (p.unitType === 'Gram') setGram({ product: p });
    else change.mutate(() => ordersApi.addItem(tableId, p.id, 1));
  };

  return (
    <div className="table-detail">
      <div className="detail-head">
        <Link to="/" className="back">← Masalar</Link>
        <h1>{t.name}</h1>
        {t.order && <span className="detail-total">{formatKurus(t.order.remainingKurus)}</span>}
      </div>
      {error && !gram && <p className="error" role="alert">{error}</p>}
      <div className="mobile-tabs">
        <button className={tab === 'order' ? 'active' : ''} onClick={() => setTab('order')}>Hesap ({t.order?.items.length ?? 0})</button>
        <button className={tab === 'products' ? 'active' : ''} onClick={() => setTab('products')}>Ürünler</button>
      </div>
      <div className={`detail-panes show-${tab}`}>
        <section className="pane-order">
          <OrderPanel
            table={t}
            disabled={disabled}
            onSetQuantity={(i, q) => change.mutate(() => ordersApi.setQuantity(i.id, q))}
            onVoid={(i) => change.mutate(() => ordersApi.voidItem(i.id))}
            onEditGram={(i) => { setError(null); setGram({ item: i }); }}
          />
        </section>
        <section className="pane-products">
          <ProductPicker disabled={disabled} onPick={(p) => { setError(null); pick(p); }} />
        </section>
      </div>
      {gram && (
        <GramKeypad
          title={'product' in gram ? gram.product.name : gram.item.productName}
          pricePerKg={'product' in gram ? gram.product.priceKurus : gram.item.unitPriceKurus}
          initial={'item' in gram ? gram.item.quantity : undefined}
          busy={change.isPending || disabled}
          error={error}
          onClose={() => { setGram(null); setError(null); }}
          onSubmit={(g) =>
            change.mutate(() =>
              'item' in gram ? ordersApi.setQuantity(gram.item.id, g) : ordersApi.addItem(tableId, gram.product.id, g),
            )
          }
        />
      )}
    </div>
  );
}
