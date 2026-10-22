import { Navigate, NavLink, Route, Routes } from 'react-router';
import { CategoriesAdmin } from './CategoriesAdmin';
import { ProductsAdmin } from './ProductsAdmin';
import { SettingsAdmin } from './SettingsAdmin';
import { TablesAdmin } from './TablesAdmin';
import { UsersAdmin } from './UsersAdmin';

export function AdminPage() {
  return (
    <div className="page">
      <h1 className="large-title">Yönetim</h1>
      <nav className="tabs" aria-label="Yönetim bölümleri">
        <NavLink to="/yonetim/urunler">Ürünler</NavLink>
        <NavLink to="/yonetim/kategoriler">Kategoriler</NavLink>
        <NavLink to="/yonetim/masalar">Masalar</NavLink>
        <NavLink to="/yonetim/kullanicilar">Kullanıcılar</NavLink>
        <NavLink to="/yonetim/ayarlar">Ayarlar</NavLink>
      </nav>
      <Routes>
        <Route index element={<Navigate to="/yonetim/urunler" replace />} />
        <Route path="urunler" element={<ProductsAdmin />} />
        <Route path="kategoriler" element={<CategoriesAdmin />} />
        <Route path="masalar" element={<TablesAdmin />} />
        <Route path="kullanicilar" element={<UsersAdmin />} />
        <Route path="ayarlar" element={<SettingsAdmin />} />
      </Routes>
    </div>
  );
}
