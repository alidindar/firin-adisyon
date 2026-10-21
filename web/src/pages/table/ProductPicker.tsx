import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ordersApi } from '../../api/orders';
import type { Product } from '../../api/types';
import { Thumb } from '../../components/Thumb';
import { formatPrice } from '../../lib/format';

export function ProductPicker({ disabled, onPick }: { disabled: boolean; onPick: (p: Product) => void }) {
  const catalog = useQuery({ queryKey: ['catalog'], queryFn: ordersApi.catalog });
  const [categoryId, setCategoryId] = useState<number | null>(null);

  if (catalog.isPending) return <p>Yükleniyor…</p>;
  if (catalog.isError) return <p className="error">{catalog.error.message}</p>;
  if (catalog.data.length === 0) return <p className="muted">Satışta ürün yok.</p>;

  const active = catalog.data.find((c) => c.id === categoryId) ?? catalog.data[0];

  return (
    <div className="picker">
      <div className="chips">
        {catalog.data.map((c) => (
          <button key={c.id} className={c.id === active.id ? 'chip active' : 'chip'} onClick={() => setCategoryId(c.id)}>
            {c.name}
          </button>
        ))}
      </div>
      <div className="product-grid">
        {active.products.map((p) => (
          <button key={p.id} className="product-card" disabled={disabled} onClick={() => onPick(p)}>
            <Thumb url={p.photoUrl} large />
            <span className="product-name">{p.name}</span>
            <small className="muted">{formatPrice(p.unitType, p.priceKurus)}</small>
          </button>
        ))}
      </div>
    </div>
  );
}
