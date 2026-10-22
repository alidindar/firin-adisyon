import { api } from './client';
import type { Product, Role, UnitType } from './types';

export interface Category { id: number; name: string; sortOrder: number; productCount: number }
export interface CategoryInput { name: string; sortOrder: number }
export interface ProductInput { categoryId: number; name: string; unitType: UnitType; priceKurus: number; isActive: boolean; sortOrder: number }
export interface TableRow { id: number; name: string; sortOrder: number; isActive: boolean }
export interface TableInput { name: string; sortOrder: number; isActive: boolean }
export interface UserRow { id: number; name: string; role: Role; isActive: boolean }
export interface UserCreateInput { name: string; pin: string; role: Role }
export interface UserUpdateInput { name: string; role: Role; isActive: boolean; pin: string | null }
export interface SettingsRow {
  businessName: string;
  backupDir: string | null;
  effectiveBackupDir: string;
  lastBackupAt: string | null;
  lastBackupError: string | null;
  lanUrls: string[];
}
export interface SettingsInput { businessName: string | null; backupDir: string | null }
export interface BackupRow { fileName: string; sizeBytes: number; createdAt: string }

const post = <T>(path: string, json: unknown) => api<T>(path, { method: 'POST', json });
const put = <T>(path: string, json: unknown) => api<T>(path, { method: 'PUT', json });

export const adminApi = {
  categories: () => api<Category[]>('/admin/categories'),
  createCategory: (i: CategoryInput) => post<Category>('/admin/categories', i),
  updateCategory: (id: number, i: CategoryInput) => put<Category>(`/admin/categories/${id}`, i),
  deleteCategory: (id: number) => api<void>(`/admin/categories/${id}`, { method: 'DELETE' }),

  products: () => api<Product[]>('/admin/products'),
  createProduct: (i: ProductInput) => post<Product>('/admin/products', i),
  updateProduct: (id: number, i: ProductInput) => put<Product>(`/admin/products/${id}`, i),
  uploadPhoto: (id: number, blob: Blob) => {
    const body = new FormData();
    body.append('file', blob, 'foto.jpg');
    return api<Product>(`/admin/products/${id}/photo`, { method: 'POST', body });
  },
  deletePhoto: (id: number) => api<Product>(`/admin/products/${id}/photo`, { method: 'DELETE' }),

  tables: () => api<TableRow[]>('/admin/tables'),
  createTable: (i: TableInput) => post<TableRow>('/admin/tables', i),
  updateTable: (id: number, i: TableInput) => put<TableRow>(`/admin/tables/${id}`, i),

  users: () => api<UserRow[]>('/admin/users'),
  createUser: (i: UserCreateInput) => post<UserRow>('/admin/users', i),
  updateUser: (id: number, i: UserUpdateInput) => put<UserRow>(`/admin/users/${id}`, i),

  settings: () => api<SettingsRow>('/admin/settings'),
  saveSettings: (i: SettingsInput) => put<SettingsRow>('/admin/settings', i),
  backups: () => api<BackupRow[]>('/admin/backups'),
  createBackup: () => api<BackupRow>('/admin/backups', { method: 'POST' }),
};
