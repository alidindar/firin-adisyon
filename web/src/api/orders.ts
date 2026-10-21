import { api } from './client';
import type { CatalogCategory, UnitType } from './types';

export type OrderStatus = 'Open' | 'Closed' | 'Cancelled';
export type PaymentMethod = 'Cash' | 'Card';

export interface OrderItem {
  id: number;
  productId: number;
  productName: string;
  unitType: UnitType;
  unitPriceKurus: number;
  /** Adet ya da gram. */
  quantity: number;
  lineTotalKurus: number;
  isPaid: boolean;
  addedAt: string;
  addedBy: string;
}

export interface PaymentRow {
  id: number;
  amountKurus: number;
  method: PaymentMethod;
  paidAt: string;
  userName: string;
}

export interface Order {
  id: number;
  status: OrderStatus;
  openedAt: string;
  totalKurus: number;
  paidKurus: number;
  remainingKurus: number;
  items: OrderItem[];
  payments: PaymentRow[];
}

export interface TableDetail {
  id: number;
  name: string;
  /** Açık adisyon; masa boşsa null. */
  order: Order | null;
}

export interface TableSummary {
  id: number;
  name: string;
  orderId: number | null;
  totalKurus: number;
  remainingKurus: number;
  openedAt: string | null;
  itemCount: number;
}

export interface PayResult {
  orderId: number;
  closed: boolean;
  payment: PaymentRow;
  table: TableDetail;
}

export interface PayBody {
  method: PaymentMethod;
  amountKurus?: number;
  itemIds?: number[];
}

export const ordersApi = {
  tables: () => api<TableSummary[]>('/tables'),
  table: (id: number) => api<TableDetail>(`/tables/${id}`),
  addItem: (tableId: number, productId: number, quantity: number) =>
    api<TableDetail>(`/tables/${tableId}/items`, { method: 'POST', json: { productId, quantity } }),
  setQuantity: (itemId: number, quantity: number) =>
    api<TableDetail>(`/items/${itemId}`, { method: 'PUT', json: { quantity } }),
  voidItem: (itemId: number) => api<TableDetail>(`/items/${itemId}`, { method: 'DELETE' }),
  pay: (tableId: number, body: PayBody) =>
    api<PayResult>(`/tables/${tableId}/payments`, { method: 'POST', json: body }),
  order: (id: number) => api<Order>(`/orders/${id}`),
  catalog: () => api<CatalogCategory[]>('/catalog'),
};
