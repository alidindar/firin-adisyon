import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { ordersApi, type OrderItem, type TableDetail } from '../../api/orders';
import type { Product } from '../../api/types';
import { Segmented } from '../../components/Segmented';
import { Toast } from '../../components/Toast';
import { formatKurus } from '../../lib/format';
import { elapsedLabel } from '../../lib/time';
import { useNow } from '../../lib/useNow';
import { useOnline } from '../../realtime/RealtimeProvider';
import { GramKeypad } from './GramKeypad';
import { OrderPanel } from './OrderPanel';
import { PaymentDialog } from './PaymentDialog';
import { ProductPicker } from './ProductPicker';

type GramTarget = { product: Product } | { item: OrderItem };
type Tab = 'order' | 'products';

const backIcon = (
  <svg viewBox="0 0 13 21" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M10.5 2.5 2.5 10.5l8 8" /></svg>
);

export function TableDetailPage() {
  const tableId = Number(useParams().id);
  const qc = useQueryClient();
  const online = useOnline();
  const now = useNow();
  const table = useQuery({
    queryKey: ['table', tableId],
    queryFn: () => ordersApi.table(tableId),
    enabled: Number.isInteger(tableId),
  });
  const [gram, setGram] = useState<GramTarget | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Seçilmediyse: dolu masa hesapla, boş masa ürünlerle açılır.
  const [tabChoice, setTab] = useState<Tab | null>(null);
  const firstTab = useRef<Tab | null>(null);
  const [paying, setPaying] = useState(false);
  const [undo, setUndo] = useState<{ itemId: number; name: string } | null>(null);

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

  const items = table.data?.order?.items;
  const inBill = useMemo(() => {
    const m = new Map<number, number>();
    for (const i of items ?? []) if (!i.isPaid) m.set(i.productId, (m.get(i.productId) ?? 0) + (i.unitType === 'Piece' ? i.quantity : 1));
    return m;
  }, [items]);

  if (!Number.isInteger(tableId))
    return (
      <div className="page">
        <p className="error">Masa bulunamadı.</p>
        <Link to="/">← Masalara dön</Link>
      </div>
    );
  if (table.isPending) return <p className="muted">Yükleniyor…</p>;
  if (table.isError)
    return (
      <div className="page">
        <p className="error">{table.error.message}</p>
        <Link to="/">← Masalara dön</Link>
      </div>
    );

  const t = table.data;
  const order = t.order;
  // İlk açılıştaki seçim sabit kalır: son kalem iptal edilince ekran kendiliğinden sekme değiştirmez.
  firstTab.current ??= order?.items.length ? 'order' : 'products';
  const tab: Tab = tabChoice ?? firstTab.current;
  const disabled = !online;
  const canPay = !!order && order.remainingKurus > 0;
  const startPay = () => { setError(null); setPaying(true); };
  const pick = (p: Product) => {
    if (p.unitType === 'Gram') setGram({ product: p });
    else change.mutate(() => ordersApi.addItem(tableId, p.id, 1));
  };
  const voidItem = (i: OrderItem) =>
    change.mutate(() => ordersApi.voidItem(i.id), {
      onSuccess: () => setUndo({ itemId: i.id, name: i.productName }),
    });

  return (
    <div className="table-detail">
      <div className="detail-top">
        <div className="detail-nav">
          <Link to="/" className="nav-back">{backIcon}Masalar</Link>
          <h1 className="nav-title">{t.name}</h1>
          <span className="nav-right">{order ? elapsedLabel(order.openedAt, now) : ''}</span>
        </div>
        <div className="seg-wrap">
          <Segmented
            label="Görünüm"
            value={tab}
            onChange={setTab}
            options={[
              ['order', <>Hesap{order?.items.length ? <span className="badge">{order.items.length}</span> : null}</>],
              ['products', 'Ürünler'],
            ]}
          />
        </div>
      </div>
      {error && !gram && !paying && <p className="error" role="alert">{error}</p>}
      <div className={`detail-panes show-${tab}`}>
        <section className="pane-order" aria-label="Hesap">
          <OrderPanel
            table={t}
            disabled={disabled || change.isPending}
            onSetQuantity={(i, q) => change.mutate(() => ordersApi.setQuantity(i.id, q))}
            onVoid={voidItem}
            onEditGram={(i) => { setError(null); setGram({ item: i }); }}
            onAddProducts={() => setTab('products')}
            footer={
              canPay ? (
                <button className="primary pay-full" disabled={disabled || change.isPending} onClick={startPay}>
                  Ödeme al · {formatKurus(order!.remainingKurus)}
                </button>
              ) : null
            }
          />
        </section>
        <section className="pane-products" aria-label="Ürünler">
          <ProductPicker disabled={disabled} inBill={inBill} onPick={(p) => { setError(null); pick(p); }} />
        </section>
      </div>
      {order && (
        <div className="paybar">
          <div>
            <span className="k">{order.paidKurus > 0 ? `Kalan · ${formatKurus(order.paidKurus)} ödendi` : 'Kalan'}</span>
            <strong>{formatKurus(order.remainingKurus)}</strong>
          </div>
          <button className="primary" disabled={!canPay || disabled || change.isPending} onClick={startPay}>Ödeme al</button>
        </div>
      )}
      {undo && (
        <Toast
          key={undo.itemId}
          message={`${undo.name} iptal edildi`}
          actionLabel="Geri al"
          onAction={() => change.mutate(() => ordersApi.restoreItem(undo.itemId))}
          onDone={() => setUndo(null)}
        />
      )}
      {paying && (
        <PaymentDialog
          table={t}
          disabled={disabled || change.isPending}
          onPaid={(r) => {
            qc.setQueryData(['table', tableId], r.table);
            qc.invalidateQueries({ queryKey: ['tables'] });
          }}
          onClose={() => setPaying(false)}
        />
      )}
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
