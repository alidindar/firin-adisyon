import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { adminApi, type Category } from '../../api/admin';
import { Modal } from '../../components/Modal';

export function CategoriesAdmin() {
  const categories = useQuery({ queryKey: ['admin', 'categories'], queryFn: adminApi.categories });
  const [editing, setEditing] = useState<Category | 'new' | null>(null);

  if (categories.isPending) return <p>Yükleniyor…</p>;
  if (categories.isError) return <p className="error">{categories.error.message}</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Kategoriler</h2>
        <button className="primary" onClick={() => setEditing('new')}>+ Yeni kategori</button>
      </div>
      {categories.data.length === 0 && <p>Henüz kategori yok. Örnek: "Kuru pasta", "Börek", "İçecekler".</p>}
      <ul className="admin-list">
        {categories.data.map((c) => (
          <li key={c.id}>
            <button className="row-btn" onClick={() => setEditing(c)}>
              <span className="row-main">
                <span>{c.name}</span>
                <small>{c.productCount} ürün · sıra {c.sortOrder}</small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {editing && <CategoryForm category={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function CategoryForm({ category, onClose }: { category: Category | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [name, setName] = useState(category?.name ?? '');
  const [sortOrder, setSortOrder] = useState(String(category?.sortOrder ?? 0));
  const [error, setError] = useState<string | null>(null);

  const done = () => {
    qc.invalidateQueries({ queryKey: ['admin'] });
    qc.invalidateQueries({ queryKey: ['catalog'] });
    onClose();
  };
  const save = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), sortOrder: Number(sortOrder) || 0 };
      return category ? adminApi.updateCategory(category.id, input) : adminApi.createCategory(input);
    },
    onSuccess: done,
    onError: (e) => setError(e.message),
  });
  const remove = useMutation({
    mutationFn: () => adminApi.deleteCategory(category!.id),
    onSuccess: done,
    onError: (e) => setError(e.message),
  });

  return (
    <Modal title={category ? 'Kategoriyi düzenle' : 'Yeni kategori'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Kategori adı<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={60} autoFocus /></label>
        <label>Sıra<input type="number" inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} /></label>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="form-actions">
          {category && (
            <button type="button" className="danger" disabled={category.productCount > 0 || remove.isPending}
              title={category.productCount > 0 ? 'İçinde ürün olan kategori silinemez' : undefined}
              onClick={() => remove.mutate()}>
              Sil
            </button>
          )}
          <button className="primary" disabled={save.isPending}>Kaydet</button>
        </div>
      </form>
    </Modal>
  );
}
