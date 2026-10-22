import type { DailyRow } from '../api/reports';

export type Preset = 'today' | 'yesterday' | 'week' | 'month';

const pad = (n: number) => String(n).padStart(2, '0');

export function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);

export function presetRange(preset: Preset, today: Date): { from: string; to: string } {
  const t = toIsoDate(today);
  switch (preset) {
    case 'today':
      return { from: t, to: t };
    case 'yesterday': {
      const y = toIsoDate(addDays(today, -1));
      return { from: y, to: y };
    }
    case 'week': {
      const sinceMonday = (today.getDay() + 6) % 7; // pazartesi = 0
      return { from: toIsoDate(addDays(today, -sinceMonday)), to: t };
    }
    case 'month':
      return { from: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 1)), to: t };
  }
}

const dayShort = new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short' });
const dateTime = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' });

/** "2026-10-21" → "21 Eki" */
export function formatDayShort(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  return dayShort.format(new Date(y, m - 1, d));
}

/** Sunucudan gelen UTC zamanı cihazın saatinde yazar. */
export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso));
}

export interface ChartBucket extends DailyRow {
  /** Kovanın son günü; günlük kovada day ile aynıdır. */
  endDay: string;
}

export const MAX_DAILY_BARS = 62;

/** 62 güne kadar günlük bırakır; daha uzun aralığı 7 günlük kovalara toplar (sütunlar telefonda sığsın diye). */
export function bucketDays(days: DailyRow[]): ChartBucket[] {
  if (days.length <= MAX_DAILY_BARS) return days.map((d) => ({ ...d, endDay: d.day }));
  const out: ChartBucket[] = [];
  for (let i = 0; i < days.length; i += 7) {
    const chunk = days.slice(i, i + 7);
    out.push({
      day: chunk[0].day,
      endDay: chunk[chunk.length - 1].day,
      revenueKurus: chunk.reduce((a, d) => a + d.revenueKurus, 0),
      cashKurus: chunk.reduce((a, d) => a + d.cashKurus, 0),
      cardKurus: chunk.reduce((a, d) => a + d.cardKurus, 0),
    });
  }
  return out;
}
