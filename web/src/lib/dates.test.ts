import { describe, expect, it } from 'vitest';
import { bucketDays, formatDayShort, presetRange, toIsoDate } from './dates';

// 22 Ekim 2026 perşembe
const today = new Date(2026, 9, 22, 15, 30);

describe('toIsoDate', () => {
  it('yerel tarihi yyyy-MM-dd yazar', () => {
    expect(toIsoDate(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
  });
});

describe('presetRange', () => {
  it.each([
    ['today', '2026-10-22', '2026-10-22'],
    ['yesterday', '2026-10-21', '2026-10-21'],
    ['week', '2026-10-19', '2026-10-22'],
    ['month', '2026-10-01', '2026-10-22'],
  ] as const)('%s', (preset, from, to) => {
    expect(presetRange(preset, today)).toEqual({ from, to });
  });

  it('pazar günü hafta önceki pazartesiden başlar', () => {
    expect(presetRange('week', new Date(2026, 9, 25))).toEqual({ from: '2026-10-19', to: '2026-10-25' });
  });
});

describe('formatDayShort', () => {
  it('gün ve kısa ay adı', () => {
    expect(formatDayShort('2026-10-21')).toBe('21 Eki');
  });
});

describe('bucketDays', () => {
  const mk = (n: number) =>
    Array.from({ length: n }, (_, i) => ({
      day: toIsoDate(new Date(2026, 0, 1 + i)),
      revenueKurus: 100,
      cashKurus: 60,
      cardKurus: 40,
    }));

  it('62 güne kadar günlük bırakır', () => {
    const out = bucketDays(mk(62));
    expect(out).toHaveLength(62);
    expect(out[0]).toMatchObject({ day: '2026-01-01', endDay: '2026-01-01' });
  });

  it('62 günden uzunu 7 günlük kovalara toplar', () => {
    const out = bucketDays(mk(70));
    expect(out).toHaveLength(10);
    expect(out[0]).toEqual({ day: '2026-01-01', endDay: '2026-01-07', revenueKurus: 700, cashKurus: 420, cardKurus: 280 });
  });

  it('son kova eksik kalabilir', () => {
    const out = bucketDays(mk(366));
    expect(out).toHaveLength(53);
    expect(out[52]).toMatchObject({ day: '2026-12-31', endDay: '2027-01-01', revenueKurus: 200 });
    expect(out.reduce((a, b) => a + b.revenueKurus, 0)).toBe(36600);
  });
});
