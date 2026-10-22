import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { reportsApi, type OrderHistoryRow } from '../../api/reports';
import { Modal } from '../../components/Modal';
import { formatDateTime } from '../../lib/dates';
import { formatKurus, formatQuantity } from '../../lib/format';

export function OrderHistory({ rows, hasMore }: { rows: OrderHistoryRow[]; hasMore: boolean }) {
  const [openId, setOpenId] = useState<number | null>(null);
  if (rows.length === 0) return <p className="muted">Bu aralıkta kapanan hesap yok.</p>;

  return (
    <>
      <ul className="admin-list">
        {rows.map((o) => (
          <li key={o.id} className={o.status === 'Cancelled' ? 'inactive' : ''}>
            <button className="row-btn" onClick={() => setOpenId(o.id)}>
              <span className="row-main">
                <span>{o.tableName} · {formatKurus(o.totalKurus)}</span>
                <small>
                  {o.closedAt ? formatDateTime(o.closedAt) : ''} · {o.itemCount} kalem
                  {o.status === 'Cancelled' && ' · iptal edildi'}
                </small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {hasMore && <p className="muted">Son {rows.length} adisyon gösteriliyor; aralığı daraltın.</p>}
      {openId !== null && <OrderDetail id={openId} onClose={() => setOpenId(null)} />}
    </>
  );
}

function OrderDetail({ id, onClose }: { id: number; onClose: () => void }) {
  const audit = useQuery({ queryKey: ['reports', 'order', id], queryFn: () => reportsApi.order(id) });
  return (
    <Modal title={audit.data ? `${audit.data.tableName} · #${id}` : `Adisyon #${id}`} onClose={onClose}>
      {audit.isPending && <p>Yükleniyor…</p>}
      {audit.isError && <p className="error">{audit.error.message}</p>}
      {audit.data && (
        <div className="audit">
          <p className="muted">Açılış {formatDateTime(audit.data.order.openedAt)}</p>
          <ul className="audit-lines">
            {audit.data.order.items.map((i) => (
              <li key={i.id}>
                <span>{i.productName} · {formatQuantity(i.unitType, i.quantity)}</span>
                <strong>{formatKurus(i.lineTotalKurus)}</strong>
              </li>
            ))}
          </ul>
          <p>Toplam: <strong>{formatKurus(audit.data.order.totalKurus)}</strong></p>
          <h3>Ödemeler</h3>
          <ul className="audit-lines">
            {audit.data.order.payments.map((p) => (
              <li key={p.id}>
                <span>{p.method === 'Cash' ? 'Nakit' : 'Kart'} · {formatDateTime(p.paidAt)} · {p.userName}</span>
                <strong>{formatKurus(p.amountKurus)}</strong>
              </li>
            ))}
            {audit.data.order.payments.length === 0 && <li className="muted">Ödeme yok</li>}
          </ul>
          {audit.data.voids.length > 0 && (
            <>
              <h3>İptal edilen kalemler</h3>
              <ul className="audit-lines voided">
                {audit.data.voids.map((v, n) => (
                  <li key={n}>
                    <span>{v.productName} · {formatQuantity(v.unitType, v.quantity)} · {v.voidedBy}, {formatDateTime(v.voidedAt)}</span>
                    <s>{formatKurus(v.lineTotalKurus)}</s>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
    </Modal>
  );
}
