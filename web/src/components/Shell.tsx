import { useQueryClient } from '@tanstack/react-query';
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { api } from '../api/client';
import type { Me } from '../api/types';
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
          {isOwner && null /* Parça 2: Yönetim, Parça 5: Raporlar bağlantıları buraya */}
        </nav>
        <div className="shell-user">
          <span>{me.name}</span>
          <button onClick={logout}>Kullanıcı değiştir</button>
        </div>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<HomePage me={me} />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
