import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { ordersApi } from '../api/orders';
import { useMe } from '../auth/useMe';
import { formatKurus } from '../lib/format';
import { elapsedLabel } from '../lib/time';
import { useNow } from '../lib/useNow';

export function TablesPage() {
  const tables = useQuery({ queryKey: ['tables'], queryFn: ordersApi.tables });
  const me = useMe().data;
  const now = useNow();
  const navigate = useNavigate();

  if (tables.isPending) return <p>Yükleniyor…</p>;
  if (tables.isError) return <p className="error">{tables.error.message}</p>;

  if (tables.data.length === 0)
    return (
      <div className="page">
        <h1 className="large-title">Masalar</h1>
        <p className="subtitle">
          Henüz masa yok.{' '}
          {me?.role === 'Owner' ? <Link to="/yonetim/masalar">Masa ekleyin</Link> : 'İşletme sahibinin masa eklemesi gerekiyor.'}
        </p>
      </div>
    );

  const busy = tables.data.filter((t) => t.orderId !== null).length;

  return (
    <div className="page">
      <h1 className="large-title">Masalar</h1>
      <p className="subtitle">{busy} dolu · {tables.data.length - busy} boş</p>
      <div className="table-grid">
        {tables.data.map((t) => (
          <button key={t.id} className={`table-card${t.orderId ? ' busy' : ' empty'}`} onClick={() => navigate(`/masa/${t.id}`)}>
            <span className="table-name">{t.name}</span>
            {t.orderId ? (
              <>
                <span className="table-total">{formatKurus(t.remainingKurus)}</span>
                <span className="table-meta">
                  <span className="dot" aria-hidden="true" />
                  {elapsedLabel(t.openedAt!, now)} · {t.itemCount} kalem
                  {t.remainingKurus !== t.totalKurus && ` · toplam ${formatKurus(t.totalKurus)}`}
                </span>
              </>
            ) : (
              <span className="table-state">Boş</span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
