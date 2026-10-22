import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { adminApi } from '../../api/admin';
import type { Product } from '../../api/types';
import { Thumb } from '../../components/Thumb';
import { formatPrice } from '../../lib/format';
import { ProductForm } from './ProductForm';

export function ProductsAdmin() {
  const products = useQuery({ queryKey: ['admin', 'products'], queryFn: adminApi.products });
  const categories = useQuery({ queryKey: ['admin', 'categories'], queryFn: adminApi.categories });
  const [editing, setEditing] = useState<Product | 'new' | null>(null);

  if (products.isPending || categories.isPending) return <p>Yükleniyor…</p>;
  if (products.isError || categories.isError) return <p className="error">{(products.error ?? categories.error)!.message}</p>;

  const cats = categories.data;
  if (cats.length === 0)
    return <p>Ürün eklemeden önce <Link to="/yonetim/kategoriler">bir kategori oluşturun</Link> (örn. "Kuru pasta", "İçecekler").</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Ürünler</h2>
        <button className="primary" onClick={() => setEditing('new')}>+ Yeni ürün</button>
      </div>
      {products.data.length === 0 && <p>Henüz ürün yok.</p>}
      {cats.map((c) => {
        const items = products.data.filter((p) => p.categoryId === c.id);
        if (items.length === 0) return null;
        return (
          <section key={c.id} className="cat-group">
            <h3>{c.name}</h3>
            <ul className="admin-list">
              {items.map((p) => (
                <li key={p.id} className={p.isActive ? '' : 'inactive'}>
                  <button className="row-btn" onClick={() => setEditing(p)}>
                    <Thumb url={p.photoUrl} />
                    <span className="row-main">
                      <span>{p.name}</span>
                      <small>{formatPrice(p.unitType, p.priceKurus)}{!p.isActive && ' · satışta değil'}</small>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
      {editing && (
        <ProductForm product={editing === 'new' ? null : editing} categories={cats} onClose={() => setEditing(null)} />
      )}
    </>
  );
}
