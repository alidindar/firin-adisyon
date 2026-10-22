import { useMemo, useState } from 'react';
import type { DailyRow } from '../../api/reports';
import { bucketDays, formatDayShort, type ChartBucket } from '../../lib/dates';
import { formatKurus } from '../../lib/format';

const rangeLabel = (b: ChartBucket, weekly: boolean) => {
  const partial = weekly && b.dayCount < 7 ? ` (${b.dayCount} gün)` : '';
  if (b.endDay === b.day) return `${formatDayShort(b.day)}${partial}`;
  return `${formatDayShort(b.day)} – ${formatDayShort(b.endDay)}${partial}`;
};

export function DailyChart({ days }: { days: DailyRow[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const buckets = useMemo(() => bucketDays(days), [days]);
  const weekly = buckets.length > 0 && buckets[0].endDay !== buckets[0].day;
  const max = Math.max(...buckets.map((d) => d.revenueKurus), 0);
  const maxIndex = buckets.findIndex((d) => d.revenueKurus === max);
  // En fazla yaklaşık 7 etiket yazılır; böylece dar ekranda etiketler çakışmaz.
  const labelEvery = Math.max(1, Math.ceil(buckets.length / 7));
  const shown = hover !== null ? buckets[hover] : null;
  const labelAlign = (i: number) => (i < buckets.length / 4 ? ' at-start' : i > (buckets.length * 3) / 4 ? ' at-end' : '');

  return (
    <figure className="chart">
      <figcaption className="chart-title">{weekly ? '7 günlük ciro toplamları' : 'Günlük ciro'}</figcaption>
      <p className="chart-tip" aria-live="polite">
        {shown
          ? `${rangeLabel(shown, weekly)}: ${formatKurus(shown.revenueKurus)} (nakit ${formatKurus(shown.cashKurus)}, kart ${formatKurus(shown.cardKurus)})`
          : 'Bir sütuna dokunun ya da üzerine gelin.'}
      </p>
      <div className="columns" onMouseLeave={() => setHover(null)}>
        {buckets.map((d, i) => (
          <button
            key={d.day}
            type="button"
            className={`col-slot${hover === i ? ' active' : ''}`}
            aria-label={`${rangeLabel(d, weekly)}: ${formatKurus(d.revenueKurus)}`}
            onMouseEnter={() => setHover(i)}
            onFocus={() => setHover(i)}
            onClick={() => setHover(i)}
          >
            <span className="col-bar" style={{ height: max ? `${(d.revenueKurus / max) * 100}%` : '0%' }} />
            {i === maxIndex && max > 0 && <span className={`col-label${labelAlign(i)}`}>{formatKurus(max)}</span>}
            {i % labelEvery === 0 && <span className={`col-x${labelAlign(i)}`}>{formatDayShort(d.day)}</span>}
          </button>
        ))}
      </div>
      <details className="table-view">
        <summary>Tablo olarak göster</summary>
        <table>
          <thead><tr><th>{weekly ? 'Dönem' : 'Gün'}</th><th>Ciro</th><th>Nakit</th><th>Kart</th></tr></thead>
          <tbody>
            {buckets.map((d) => (
              <tr key={d.day}>
                <td>{rangeLabel(d, weekly)}</td>
                <td>{formatKurus(d.revenueKurus)}</td>
                <td>{formatKurus(d.cashKurus)}</td>
                <td>{formatKurus(d.cardKurus)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}
