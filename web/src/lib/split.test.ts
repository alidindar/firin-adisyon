import { describe, expect, it } from 'vitest';
import { cashSuggestions, changeDue, remainingShares, splitEqually } from './split';

describe('splitEqually', () => {
  it('kuruş artığını son kişiye ekler', () => {
    expect(splitEqually(10000, 3)).toEqual([3333, 3333, 3334]);
    expect(splitEqually(5, 3)).toEqual([1, 1, 3]);
  });
  it('tam bölünürse eşit böler', () => {
    expect(splitEqually(18000, 2)).toEqual([9000, 9000]);
    expect(splitEqually(18000, 1)).toEqual([18000]);
  });
  it('geçersiz girişte boş döner', () => {
    expect(splitEqually(0, 3)).toEqual([]);
    expect(splitEqually(100, 0)).toEqual([]);
  });
});

describe('changeDue', () => {
  it('para üstünü hesaplar, eksikte 0 döner', () => {
    expect(changeDue(20000, 18000)).toBe(2000);
    expect(changeDue(10000, 18000)).toBe(0);
  });
});

describe('cashSuggestions', () => {
  it('tam tutar ve yuvarlak banknotlar', () => {
    expect(cashSuggestions(18000)).toEqual([18000, 20000, 50000, 100000]);
    expect(cashSuggestions(20000)).toEqual([20000, 50000, 100000, 200000]);
    expect(cashSuggestions(4250)).toEqual([4250, 5000, 10000, 20000]);
  });
});

describe('remainingShares', () => {
  it('ödeyenleri düşüp kalanı kalan kişilere böler', () => {
    expect(remainingShares(10000, 3, 0)).toEqual([3333, 3333, 3334]);
    expect(remainingShares(6667, 3, 1)).toEqual([3333, 3334]);
    expect(remainingShares(3334, 3, 2)).toEqual([3334]);
  });
  it('herkes ödemiş görünse bile en az bir pay kalır', () => {
    expect(remainingShares(500, 2, 5)).toEqual([500]);
  });
});
