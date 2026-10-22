import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { reportsApi } from '../../api/reports';
import { useNow } from '../../lib/useNow';
import { formatDateTime, presetRange, rangeProblem, type Preset } from '../../lib/dates';
import { formatKurus, formatQuantity } from '../../lib/format';
import { DailyChart } from './DailyChart';
import { OrderHistory } from './OrderHistory';
import { ProductBars } from './ProductBars';

const presets: [Preset, string][] = [
  ['today', 'Bugün'],
  ['yesterday', 'Dün'],
  ['week', 'Bu hafta'],
  ['month', 'Bu ay'],
];

export function ReportsPage() {
  const [preset, setPreset] = useState<Preset | 'custom'>('today');
  const [custom, setCustom] = useState(() => presetRange('today', new Date()));
  // Hazır aralıklar her çizimde yerel tarihten türetilir: gece yarısından sonra "Bugün" yeni güne geçer.
  const now = useNow(60_000);
  const range = preset === 'custom' ? custom : presetRange(preset, now);
  const problem = rangeProblem(range.from, range.to);
  const summary = useQuery({
    enabled: !problem,
    queryKey: ['reports', 'summary', range.from, range.to],
    queryFn: () => reportsApi.summary(range.from, range.to),
  });
  const orders = useQuery({
    enabled: !problem,
    queryKey: ['reports', 'orders', range.from, range.to],
    queryFn: () => reportsApi.orders(range.from, range.to),
  });

  const choose = (p: Preset) => {
    setPreset(p);
  };
  const setDate = (key: 'from' | 'to', value: string) => {
    if (!value) return;
    setCustom({ ...range, [key]: value });
    setPreset('custom');
  };

  const s = summary.data;

  return (
    <div className="page reports">
      <h1>Raporlar</h1>
      <div className="filters">
        <div className="chips">
          {presets.map(([p, label]) => (
            <button key={p} className={preset === p ? 'chip active' : 'chip'} onClick={() => choose(p)}>{label}</button>
          ))}
        </div>
        <label>Başlangıç<input type="date" value={range.from} max={range.to} onChange={(e) => setDate('from', e.target.value)} /></label>
        <label>Bitiş<input type="date" value={range.to} min={range.from} onChange={(e) => setDate('to', e.target.value)} /></label>
      </div>

      {problem && <p className="range-hint error" role="alert">{problem}</p>}
      {!problem && summary.isPending && <p>Yükleniyor…</p>}
      {!problem && summary.isError && <p className="error">{summary.error.message}</p>}
      {!problem && s && (
        <>
          <div className="stats">
            <div className="stat hero"><span>Ciro</span><strong>{formatKurus(s.revenueKurus)}</strong></div>
            <div className="stat"><span>Nakit</span><strong>{formatKurus(s.cashKurus)}</strong></div>
            <div className="stat"><span>Kart</span><strong>{formatKurus(s.cardKurus)}</strong></div>
            <div className="stat"><span>Kapanan hesap</span><strong>{s.closedOrders}</strong></div>
            <div className="stat"><span>Ortalama hesap</span><strong>{formatKurus(s.averageOrderKurus)}</strong></div>
            <div className="stat"><span>İptal edilen hesap</span><strong>{s.cancelledOrders}</strong></div>
          </div>
          <p className="muted note">Ciro, bu aralıkta alınan ödemelerin toplamıdır. Ürün satışları kapanan hesaplardan hesaplanır.</p>

          {s.days.length > 1 && <DailyChart days={s.days} />}
          <ProductBars products={s.products} />

          <section>
            <h2>İptal edilen kalemler ({s.voids.length})</h2>
            {s.voids.length === 0 ? (
              <p className="muted">İptal yok.</p>
            ) : (
              <ul className="audit-lines voided">
                {s.voids.map((v, n) => (
                  <li key={n}>
                    <span>{formatDateTime(v.voidedAt)} · {v.tableName} · {v.productName} · {formatQuantity(v.unitType, v.quantity)} · {v.voidedBy}</span>
                    <s>{formatKurus(v.lineTotalKurus)}</s>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}

      <section>
        <h2>Hesap geçmişi</h2>
        {!problem && orders.isPending && <p>Yükleniyor…</p>}
        {!problem && orders.isError && <p className="error">{orders.error.message}</p>}
        {!problem && orders.data && <OrderHistory rows={orders.data.orders} hasMore={orders.data.hasMore} />}
      </section>
    </div>
  );
}
