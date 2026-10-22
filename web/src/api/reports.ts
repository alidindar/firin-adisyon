import { api } from './client';
import type { Order, OrderStatus } from './orders';
import type { UnitType } from './types';

export interface ProductSales { productId: number; productName: string; unitType: UnitType; quantity: number; totalKurus: number }
export interface VoidRow { orderId: number; voidedAt: string; tableName: string; productName: string; unitType: UnitType; quantity: number; lineTotalKurus: number; voidedBy: string }
export interface DailyRow { day: string; revenueKurus: number; cashKurus: number; cardKurus: number }
export interface Summary {
  from: string;
  to: string;
  revenueKurus: number;
  cashKurus: number;
  cardKurus: number;
  closedOrders: number;
  cancelledOrders: number;
  averageOrderKurus: number;
  products: ProductSales[];
  voids: VoidRow[];
  days: DailyRow[];
}
export interface OrderHistoryRow { id: number; tableName: string; status: OrderStatus; openedAt: string; closedAt: string | null; totalKurus: number; paidKurus: number; itemCount: number }
export interface OrderHistory { orders: OrderHistoryRow[]; hasMore: boolean }
export interface OrderAudit { order: Order; tableName: string; voids: VoidRow[] }

const q = (from: string, to: string) => `?from=${from}&to=${to}`;

export const reportsApi = {
  summary: (from: string, to: string) => api<Summary>(`/reports/summary${q(from, to)}`),
  orders: (from: string, to: string) => api<OrderHistory>(`/reports/orders${q(from, to)}`),
  order: (id: number) => api<OrderAudit>(`/reports/orders/${id}`),
};
