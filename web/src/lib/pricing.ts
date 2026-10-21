import type { UnitType } from '../api/types';

/** Sunucudaki Pricing.LineTotal'ın aynısı. Tutarı sunucu belirler; bu yalnızca önizleme. */
export function lineTotal(unit: UnitType, unitPriceKurus: number, quantity: number): number {
  return unit === 'Piece' ? unitPriceKurus * quantity : Math.round((quantity * unitPriceKurus) / 1000);
}
