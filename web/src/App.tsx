import { useMe } from './auth/useMe';
import { Shell } from './components/Shell';
import { LoginPage } from './pages/LoginPage';

export function App() {
  const me = useMe();
  if (me.isPending) return <div className="center">Yükleniyor…</div>;
  if (me.isError) return <div className="center error">{me.error.message}</div>;
  if (!me.data) return <LoginPage />;
  return <Shell me={me.data} />;
}
