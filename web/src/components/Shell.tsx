import { useQueryClient } from '@tanstack/react-query';
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { api } from '../api/client';
import type { Me } from '../api/types';
import { AdminPage } from '../pages/admin/AdminPage';
import { HomePage } from '../pages/HomePage';

export function Shell({ me }: { me: Me }) {
  const qc = useQueryClient();
  const isOwner = me.role === 'Owner';

  const logout = async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => undefined);
    qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' });
    qc.setQueryData(['me'], null);
  };

  return (
    <>
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
      <main>
        <Routes>
          <Route path="/" element={<HomePage me={me} />} />
          {isOwner && <Route path="/yonetim/*" element={<AdminPage />} />}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
