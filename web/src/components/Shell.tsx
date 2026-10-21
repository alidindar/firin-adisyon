import { useQueryClient } from '@tanstack/react-query';
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { api } from '../api/client';
import type { Me } from '../api/types';
import { AdminPage } from '../pages/admin/AdminPage';
import { TablesPage } from '../pages/TablesPage';
import { TableDetailPage } from '../pages/table/TableDetailPage';
import { RealtimeProvider, useConnection } from '../realtime/RealtimeProvider';

export function Shell({ me }: { me: Me }) {
  return (
    <RealtimeProvider>
      <ShellLayout me={me} />
    </RealtimeProvider>
  );
}

function ShellLayout({ me }: { me: Me }) {
  const qc = useQueryClient();
  const connection = useConnection();
  const isOwner = me.role === 'Owner';

  const logout = async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => undefined);
    qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' });
    qc.setQueryData(['me'], null);
  };

  return (
    <>
      <div className="shell-top">
        <header className="shell-header">
          <nav>
            <NavLink to="/" end>Masalar</NavLink>
            {isOwner && <NavLink to="/yonetim">Yönetim</NavLink>}
          </nav>
          <div className="shell-user">
            <span>{me.name}</span>
            <button onClick={logout}>Kullanıcı değiştir</button>
          </div>
        </header>
        {connection === 'reconnecting' && (
          <div className="banner" role="status">Kasa bilgisayarına bağlantı yok. Yeniden bağlanılıyor…</div>
        )}
      </div>
      <main>
        <Routes>
          <Route path="/" element={<TablesPage />} />
          <Route path="/masa/:id" element={<TableDetailPage />} />
          {isOwner && <Route path="/yonetim/*" element={<AdminPage />} />}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
