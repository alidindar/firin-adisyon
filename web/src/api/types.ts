export type Role = 'Owner' | 'Staff';
export type UnitType = 'Piece' | 'Gram';

export interface Me {
  id: number;
  name: string;
  role: Role;
}

export interface UserOption {
  id: number;
  name: string;
}

export interface AuthStatus {
  needsSetup: boolean;
  users: UserOption[];
}

export interface Product {
  id: number;
  categoryId: number;
  name: string;
  unitType: UnitType;
  /** Adetli üründe adet fiyatı, gramlı üründe kilogram fiyatı (kuruş). */
  priceKurus: number;
  photoUrl: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface CatalogCategory {
  id: number;
  name: string;
  products: Product[];
}
