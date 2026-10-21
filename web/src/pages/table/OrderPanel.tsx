import { useEffect, useState, type ReactNode } from 'react';
import type { OrderItem, TableDetail } from '../../api/orders';
import { formatKurus, formatPrice, formatQuantity } from '../../lib/format';

interface Props {
  table: TableDetail;
  disabled: boolean;
  onSetQuantity: (item: OrderItem, quantity: number) => void;
  onVoid: (item: OrderItem) => void;
  onEditGram: (item: OrderItem) => void;
  footer?: ReactNode;
}

export function OrderPanel({ table, disabled, onSetQuantity, onVoid, onEditGram, footer }: Props) {
  const order = table.order;
  // İptal iki dokunuşla olur: önce çöp kutusu, sonra "İptal et?". 3 sn içinde onaylanmazsa geri döner.
  const [confirmVoid, setConfirmVoid] = useState<number | null>(null);
  useEffect(() => {
    if (confirmVoid === null) return;
    const id = window.setTimeout(() => setConfirmVoid(null), 3000);
    return () => window.clearTimeout(id);
  }, [confirmVoid]);

  return (
    <div className="order-panel">
      {!order || order.items.length === 0 ? (
        <p className="muted empty">Henüz ürün eklenmedi. Ürünlere dokunarak ekleyin.</p>
      ) : (
        <ul className="order-lines">
          {order.items.map((i) => (
            <li key={i.id} className={i.isPaid ? 'paid' : ''}>
              <div className="line-main">
                <span className="line-name">{i.productName}</span>
                <small className="muted">
                  {formatQuantity(i.unitType, i.quantity)} × {formatPrice(i.unitType, i.unitPriceKurus)}
                  {i.isPaid && ' · ödendi'}
                </small>
              </div>
              <strong className="line-total">{formatKurus(i.lineTotalKurus)}</strong>
              {!i.isPaid && (
                <div className="line-actions">
                  {i.unitType === 'Piece' ? (
                    <>
                      <button aria-label="Bir azalt" disabled={disabled || i.quantity <= 1} onClick={() => onSetQuantity(i, i.quantity - 1)}>−</button>
                      <span className="qty">{i.quantity}</span>
                      <button aria-label="Bir artır" disabled={disabled} onClick={() => onSetQuantity(i, i.quantity + 1)}>+</button>
                    </>
                  ) : (
                    <button disabled={disabled} onClick={() => onEditGram(i)}>Gramı düzelt</button>
                  )}
                  {confirmVoid === i.id ? (
                    <button className="danger" disabled={disabled} onClick={() => { setConfirmVoid(null); onVoid(i); }}>İptal et?</button>
                  ) : (
                    <button className="icon" aria-label="Kalemi iptal et" disabled={disabled} onClick={() => setConfirmVoid(i.id)}>🗑</button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {order && (
        <div className="order-summary">
          <div><span>Toplam</span><strong>{formatKurus(order.totalKurus)}</strong></div>
          {order.paidKurus > 0 && (
            <>
              <div><span>Ödenen</span><span>{formatKurus(order.paidKurus)}</span></div>
              <div className="remaining"><span>Kalan</span><strong>{formatKurus(order.remainingKurus)}</strong></div>
            </>
          )}
        </div>
      )}
      {footer}
    </div>
  );
}
