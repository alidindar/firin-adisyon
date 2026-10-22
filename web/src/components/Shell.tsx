import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { api } from '../api/client';
import type { Me } from '../api/types';
import { AdminPage } from '../pages/admin/AdminPage';
import { ReportsPage } from '../pages/reports/ReportsPage';
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
  // Yapışkan üst bölümün gerçek yüksekliği; masa detayındaki yapışkan panel buna göre konumlanır.
  const topRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = topRef.current;
    if (!el) return;
    const set = () => document.documentElement.style.setProperty('--shell-top-h', `${el.offsetHeight}px`);
    set();
    const ro = new ResizeObserver(set);
    ro.observe(el);
    return () => {
      ro.disconnect();
      document.documentElement.style.removeProperty('--shell-top-h');
    };
  }, []);

  const logout = async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => undefined);
    qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' });
    qc.setQueryData(['me'], null);
  };

  return (
    <>
      <div className="shell-top" ref={topRef}>
        <header className="shell-header">
          <nav>
            <NavLink to="/" end>Masalar</NavLink>
            {isOwner && <NavLink to="/raporlar">Raporlar</NavLink>}
            {isOwner && <NavLink to="/yonetim">Yönetim</NavLink>}
          </nav>
          <div className="shell-user">
            <span>{me.name}</span>
            <button onClick={logout} aria-label="Çıkış — kullanıcı değiştir">
              <span className="lbl-long">Kullanıcı değiştir</span>
              <span className="lbl-short" aria-hidden="true">Çıkış</span>
            </button>
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
          {isOwner && <Route path="/raporlar" element={<ReportsPage />} />}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
