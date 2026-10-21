import { describe, expect, it } from 'vitest';
import { formatKurus, formatQuantity, parseTlToKurus } from './format';

describe('formatKurus', () => {
  it('Türk lirası biçiminde yazar', () => {
    expect(formatKurus(123450)).toBe('₺1.234,50');
    expect(formatKurus(0)).toBe('₺0,00');
    expect(formatKurus(5)).toBe('₺0,05');
  });
});

describe('parseTlToKurus', () => {
  it.each([
    ['12,50', 1250],
    ['12.5', 1250],
    ['12', 1200],
    ['1.234,50', 123450],
    [' ₺7,05 ', 705],
    ['0,1', 10],
  ])('%s → %d kuruş', (input, expected) => {
    expect(parseTlToKurus(input)).toBe(expected);
  });

  it.each(['', 'abc', '12,345', '-5', '1.234'])('"%s" geçersiz', (input) => {
    expect(parseTlToKurus(input)).toBeNull();
  });
});

describe('formatQuantity', () => {
  it('adet ve gramı okunur yazar', () => {
    expect(formatQuantity('Piece', 3)).toBe('3 adet');
    expect(formatQuantity('Gram', 250)).toBe('250 g');
    expect(formatQuantity('Gram', 1250)).toBe('1,25 kg');
    expect(formatQuantity('Gram', 1000)).toBe('1 kg');
  });
});
