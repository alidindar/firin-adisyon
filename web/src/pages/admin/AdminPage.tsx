import { Navigate, NavLink, Route, Routes } from 'react-router';
import { CategoriesAdmin } from './CategoriesAdmin';
import { ProductsAdmin } from './ProductsAdmin';
import { TablesAdmin } from './TablesAdmin';
import { UsersAdmin } from './UsersAdmin';

export function AdminPage() {
  return (
    <div className="page">
      <nav className="tabs">
        <NavLink to="/yonetim/urunler">Ürünler</NavLink>
        <NavLink to="/yonetim/kategoriler">Kategoriler</NavLink>
        <NavLink to="/yonetim/masalar">Masalar</NavLink>
        <NavLink to="/yonetim/kullanicilar">Kullanıcılar</NavLink>
      </nav>
      <Routes>
        <Route index element={<Navigate to="/yonetim/urunler" replace />} />
        <Route path="urunler" element={<ProductsAdmin />} />
        <Route path="kategoriler" element={<CategoriesAdmin />} />
        <Route path="masalar" element={<TablesAdmin />} />
        <Route path="kullanicilar" element={<UsersAdmin />} />
      </Routes>
    </div>
  );
}
