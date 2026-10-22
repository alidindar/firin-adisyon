import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { Navigate, NavLink, Route, Routes, useLocation } from 'react-router';
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
  const { pathname } = useLocation();
  const inTable = pathname.startsWith('/masa/');
  const section = pathname.startsWith('/raporlar') ? 'Raporlar' : pathname.startsWith('/yonetim') ? 'Yönetim' : 'Masalar';
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

  // Telefonda büyük başlık kaydırılınca üst çubukta küçük başlık belirir.
  useEffect(() => {
    const html = document.documentElement;
    const onScroll = () => html.toggleAttribute('data-scrolled', window.scrollY > 36);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      window.removeEventListener('scroll', onScroll);
      html.removeAttribute('data-scrolled');
    };
  }, [pathname]);

  const logout = async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => undefined);
    qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' });
    qc.setQueryData(['me'], null);
  };

  return (
    <div className={inTable ? 'in-table' : undefined}>
      <div className="shell-top" ref={topRef}>
        <header className="shell-header">
          <nav>
            <NavLink to="/" end className={({ isActive }) => (isActive || inTable ? 'active' : undefined)}>Masalar</NavLink>
            {isOwner && <NavLink to="/raporlar">Raporlar</NavLink>}
            {isOwner && <NavLink to="/yonetim">Yönetim</NavLink>}
          </nav>
          <div className="topbar-title" aria-hidden="true">{section}</div>
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
      {!inTable && (
        <nav className="tabbar" aria-label="Ana bölümler">
          <NavLink to="/" end>
            <svg viewBox="0 0 26 24" fill="currentColor" aria-hidden="true"><rect x="2" y="2" width="9.5" height="9" rx="2.5" /><rect x="14.5" y="2" width="9.5" height="9" rx="2.5" /><rect x="2" y="13.5" width="9.5" height="9" rx="2.5" /><rect x="14.5" y="13.5" width="9.5" height="9" rx="2.5" /></svg>
            Masalar
          </NavLink>
          {isOwner && (
            <NavLink to="/raporlar">
              <svg viewBox="0 0 26 24" fill="currentColor" aria-hidden="true"><rect x="3" y="12" width="5" height="10" rx="1.5" /><rect x="10.5" y="6" width="5" height="16" rx="1.5" /><rect x="18" y="2" width="5" height="20" rx="1.5" /></svg>
              Raporlar
            </NavLink>
          )}
          {isOwner && (
            <NavLink to="/yonetim">
              <svg viewBox="0 0 26 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M3 6h20M3 12h20M3 18h20" /><circle cx="9" cy="6" r="2.5" fill="var(--bg)" /><circle cx="17" cy="12" r="2.5" fill="var(--bg)" /><circle cx="7" cy="18" r="2.5" fill="var(--bg)" /></svg>
              Yönetim
            </NavLink>
          )}
          <button type="button" onClick={logout} aria-label={`Çıkış — ${me.name}`}>
            <svg viewBox="0 0 26 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><circle cx="13" cy="12" r="10" /><circle cx="13" cy="9.5" r="3.5" /><path d="M6.5 19c1.5-2.6 3.8-4 6.5-4s5 1.4 6.5 4" /></svg>
            Çıkış
          </button>
        </nav>
      )}
    </div>
  );
}
