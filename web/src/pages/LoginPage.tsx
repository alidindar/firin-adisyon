import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { AuthStatus, Me, UserOption } from '../api/types';
import { PinPad } from '../components/PinPad';

export function LoginPage() {
  const status = useQuery({ queryKey: ['auth-status'], queryFn: () => api<AuthStatus>('/auth/status') });

  if (status.isPending) return <div className="center">Yükleniyor…</div>;
  if (status.isError) return <div className="center error">{status.error.message}</div>;
  if (status.data.needsSetup) return <SetupForm />;
  return <UserLogin users={status.data.users} />;
}

function UserLogin({ users }: { users: UserOption[] }) {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<UserOption | null>(null);
  const [error, setError] = useState<string | null>(null);
  const login = useMutation({
    mutationFn: (pin: string) => api<Me>('/auth/login', { method: 'POST', json: { userId: selected!.id, pin } }),
    onSuccess: (me) => qc.setQueryData(['me'], me),
    onError: (e) => setError(e.message),
  });

  return (
    <div className="login">
      <h1>Fırın Adisyon</h1>
      {!selected ? (
        <>
          <p>Kim giriş yapıyor?</p>
          <div className="user-grid">
            {users.map((u) => (
              <button key={u.id} className="user-btn" onClick={() => { setSelected(u); setError(null); }}>
                {u.name}
              </button>
            ))}
          </div>
        </>
      ) : (
        <>
          <p><strong>{selected.name}</strong>, PIN'ini gir</p>
          <PinPad disabled={login.isPending} error={error} onComplete={(pin) => { setError(null); login.mutate(pin); }} />
          <button className="link" onClick={() => setSelected(null)}>Başka kullanıcı</button>
        </>
      )}
    </div>
  );
}

function SetupForm() {
  const qc = useQueryClient();
  const [name, setName] = useState('');
  const [pin, setPin] = useState('');
  const [pin2, setPin2] = useState('');
  const [error, setError] = useState<string | null>(null);
  const setup = useMutation({
    mutationFn: () => api<Me>('/auth/setup', { method: 'POST', json: { name: name.trim(), pin } }),
    onSuccess: (me) => qc.setQueryData(['me'], me),
    onError: (e) => setError(e.message),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (pin !== pin2) return setError('PIN\'ler aynı değil.');
    setError(null);
    setup.mutate();
  };

  return (
    <div className="login">
      <h1>Fırın Adisyon — İlk kurulum</h1>
      <p>İşletme sahibinin hesabını oluştur. Ürünleri, fiyatları ve raporları yalnızca bu hesap yönetir.</p>
      <form className="form" onSubmit={submit}>
        <label>Adın<input value={name} onChange={(e) => setName(e.target.value)} required autoFocus /></label>
        <label>PIN (4 hane)<input type="password" inputMode="numeric" pattern="\d{4}" maxLength={4} value={pin} onChange={(e) => setPin(e.target.value)} required /></label>
        <label>PIN tekrar<input type="password" inputMode="numeric" pattern="\d{4}" maxLength={4} value={pin2} onChange={(e) => setPin2(e.target.value)} required /></label>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={setup.isPending}>Hesabı oluştur</button>
      </form>
    </div>
  );
}
