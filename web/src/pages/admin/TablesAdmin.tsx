import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { adminApi, type TableRow } from '../../api/admin';
import { Modal } from '../../components/Modal';

export function TablesAdmin() {
  const tables = useQuery({ queryKey: ['admin', 'tables'], queryFn: adminApi.tables });
  const [editing, setEditing] = useState<TableRow | 'new' | null>(null);

  if (tables.isPending) return <p>Yükleniyor…</p>;
  if (tables.isError) return <p className="error">{tables.error.message}</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Masalar</h2>
        <div className="toolbar-actions">
          <BulkAdd existing={tables.data} />
          <button className="primary" onClick={() => setEditing('new')}>+ Yeni masa</button>
        </div>
      </div>
      <p className="hint">Tezgâhtan satış için "Tezgâh" adında bir masa açabilirsiniz.</p>
      <ul className="admin-list">
        {tables.data.map((t) => (
          <li key={t.id} className={t.isActive ? '' : 'inactive'}>
            <button className="row-btn" onClick={() => setEditing(t)}>
              <span className="row-main">
                <span>{t.name}</span>
                <small>sıra {t.sortOrder}{!t.isActive && ' · kullanımda değil'}</small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {editing && <TableForm table={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function useInvalidateTables() {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: ['admin', 'tables'] });
    qc.invalidateQueries({ queryKey: ['tables'] });
  };
}

/** "Masa 1", "Masa 2"… adlarından kullanılmayanları sırayla ekler. */
function BulkAdd({ existing }: { existing: TableRow[] }) {
  const invalidate = useInvalidateTables();
  const [count, setCount] = useState('5');
  const [error, setError] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: async () => {
      const n = Math.min(Math.max(Number(count) || 0, 1), 50);
      const names = new Set(existing.map((t) => t.name));
      const maxSort = existing.reduce((m, t) => Math.max(m, t.sortOrder), 0);
      for (let k = 1, added = 0; added < n; k++) {
        const name = `Masa ${k}`;
        if (names.has(name)) continue;
        await adminApi.createTable({ name, sortOrder: maxSort + added + 1, isActive: true });
        added++;
      }
    },
    onSettled: invalidate,
    onError: (e) => setError(e.message),
  });

  return (
    <form className="bulk-add" onSubmit={(e) => { e.preventDefault(); setError(null); add.mutate(); }}>
      <input type="number" min={1} max={50} value={count} onChange={(e) => setCount(e.target.value)} aria-label="Eklenecek masa sayısı" />
      <button disabled={add.isPending}>masa ekle</button>
      {error && <span className="error">{error}</span>}
    </form>
  );
}

function TableForm({ table, onClose }: { table: TableRow | null; onClose: () => void }) {
  const invalidate = useInvalidateTables();
  const [name, setName] = useState(table?.name ?? '');
  const [sortOrder, setSortOrder] = useState(String(table?.sortOrder ?? 0));
  const [isActive, setIsActive] = useState(table?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), sortOrder: Number(sortOrder) || 0, isActive };
      return table ? adminApi.updateTable(table.id, input) : adminApi.createTable(input);
    },
    onSuccess: () => { invalidate(); onClose(); },
    onError: (e) => setError(e.message),
  });

  return (
    <Modal title={table ? 'Masayı düzenle' : 'Yeni masa'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Masa adı<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={30} autoFocus /></label>
        <label>Sıra<input type="number" inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} /></label>
        <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> Kullanımda</label>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={save.isPending}>Kaydet</button>
      </form>
    </Modal>
  );
}
