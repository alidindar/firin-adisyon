import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { adminApi, type UserRow } from '../../api/admin';
import type { Role } from '../../api/types';
import { Modal } from '../../components/Modal';

const roleLabel: Record<Role, string> = { Owner: 'Sahip', Staff: 'Garson' };

export function UsersAdmin() {
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: adminApi.users });
  const [editing, setEditing] = useState<UserRow | 'new' | null>(null);

  if (users.isPending) return <p>Yükleniyor…</p>;
  if (users.isError) return <p className="error">{users.error.message}</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Kullanıcılar</h2>
        <button className="primary" onClick={() => setEditing('new')}>+ Yeni kullanıcı</button>
      </div>
      <p className="hint">Garsonlar masaya ürün ekleyip hesap kapatabilir. Ürün, fiyat ve raporları yalnızca sahipler görür.</p>
      <ul className="admin-list">
        {users.data.map((u) => (
          <li key={u.id} className={u.isActive ? '' : 'inactive'}>
            <button className="row-btn" onClick={() => setEditing(u)}>
              <span className="row-main">
                <span>{u.name}</span>
                <small>{roleLabel[u.role]}{!u.isActive && ' · giriş kapalı'}</small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {editing && <UserForm user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function UserForm({ user, onClose }: { user: UserRow | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [name, setName] = useState(user?.name ?? '');
  const [role, setRole] = useState<Role>(user?.role ?? 'Staff');
  const [isActive, setIsActive] = useState(user?.isActive ?? true);
  const [pin, setPin] = useState('');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      user
        ? adminApi.updateUser(user.id, { name: name.trim(), role, isActive, pin: pin || null })
        : adminApi.createUser({ name: name.trim(), role, pin }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin', 'users'] });
      qc.invalidateQueries({ queryKey: ['auth-status'] });
      onClose();
    },
    onError: (e) => setError(e.message),
  });

  return (
    <Modal title={user ? 'Kullanıcıyı düzenle' : 'Yeni kullanıcı'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Ad<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={30} autoFocus /></label>
        <fieldset className="segmented">
          <legend>Rol</legend>
          <label><input type="radio" name="role" checked={role === 'Staff'} onChange={() => setRole('Staff')} /> Garson</label>
          <label><input type="radio" name="role" checked={role === 'Owner'} onChange={() => setRole('Owner')} /> Sahip</label>
        </fieldset>
        <label>
          {user ? 'Yeni PIN (boş bırakılırsa değişmez)' : 'PIN (4 hane)'}
          <input type="password" inputMode="numeric" pattern="\d{4}" maxLength={4} value={pin}
            onChange={(e) => setPin(e.target.value)} required={!user} autoComplete="new-password" />
        </label>
        {user && <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> Giriş yapabilir</label>}
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={save.isPending}>Kaydet</button>
      </form>
    </Modal>
  );
}
