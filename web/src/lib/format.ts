import type { UnitType } from '../api/types';

const tl = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' });
const kg = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 3 });

export function formatKurus(kurus: number): string {
  return tl.format(kurus / 100);
}

/** "12,50", "12.5", "1.234,50", "₺7" → kuruş. Anlaşılmayan girişte null. */
export function parseTlToKurus(input: string): number | null {
  const s = input.replace(/[\s₺]/g, '');
  if (!s) return null;
  const normalized = s.includes(',') ? s.replace(/\./g, '').replace(',', '.') : s;
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  const [whole, frac = ''] = normalized.split('.');
  return Number(whole) * 100 + Number(frac.padEnd(2, '0'));
}

export function formatQuantity(unitType: UnitType, quantity: number): string {
  if (unitType === 'Piece') return `${quantity} adet`;
  if (quantity < 1000) return `${quantity} g`;
  return `${kg.format(quantity / 1000)} kg`;
}
