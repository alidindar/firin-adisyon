import { describe, expect, it } from 'vitest';
import { lineTotal } from './pricing';

describe('lineTotal (sunucu PricingTests ile aynı örnekler)', () => {
  it.each([
    ['Piece', 2500, 3, 7500],
    ['Gram', 60000, 250, 15000],
    ['Gram', 45000, 333, 14985],
    ['Gram', 1234, 125, 154],
    ['Gram', 1000, 500, 500],
    ['Gram', 1001, 500, 501],
    ['Gram', 99999, 99999, 9999800],
  ] as const)('%s %d × %d = %d', (unit, price, qty, expected) => {
    expect(lineTotal(unit, price, qty)).toBe(expected);
  });
});
