import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ordersApi } from '../../api/orders';
import type { Product } from '../../api/types';
import { formatKurus } from '../../lib/format';

interface Props {
  disabled: boolean;
  onPick: (p: Product) => void;
  /** Ürün kimliğine göre bu masadaki ödenmemiş adet (gramlıda kalem sayısı). */
  inBill?: ReadonlyMap<number, number>;
}

export function ProductPicker({ disabled, onPick, inBill }: Props) {
  const catalog = useQuery({ queryKey: ['catalog'], queryFn: ordersApi.catalog });
  const [categoryId, setCategoryId] = useState<number | null>(null);

  if (catalog.isPending) return <p className="muted">Yükleniyor…</p>;
  if (catalog.isError) return <p className="error">{catalog.error.message}</p>;
  if (catalog.data.length === 0) return <p className="muted">Satışta ürün yok.</p>;

  const active = catalog.data.find((c) => c.id === categoryId) ?? catalog.data[0];

  return (
    <div className="picker">
      <div className="chips">
        {catalog.data.map((c) => (
          <button key={c.id} className={c.id === active.id ? 'chip active' : 'chip'} aria-pressed={c.id === active.id} onClick={() => setCategoryId(c.id)}>
            {c.name}
          </button>
        ))}
      </div>
      <div className="product-grid">
        {active.products.map((p) => {
          const n = inBill?.get(p.id);
          return (
            <button key={p.id} className="product-card" disabled={disabled} onClick={() => onPick(p)}>
              {n ? <span className="inbill" aria-label={`hesapta ${n}`}>{n}</span> : null}
              <span className="ph" aria-hidden="true">
                {p.photoUrl ? <img src={p.photoUrl} alt="" loading="lazy" /> : p.name.charAt(0)}
              </span>
              <span className="pbody">
                <span className="product-name">{p.name}</span>
                <span className="product-price">
                  {formatKurus(p.priceKurus)}
                  {p.unitType === 'Gram' && <span className="kg"><span className="sr-only">/ </span>kg</span>}
                </span>
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
