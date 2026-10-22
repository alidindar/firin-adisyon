import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router';
import { ordersApi, type PayBody, type PayResult, type TableDetail } from '../../api/orders';
import { Modal } from '../../components/Modal';
import { Segmented } from '../../components/Segmented';
import { formatKurus, formatQuantity, parseTlToKurus } from '../../lib/format';
import { cashSuggestions, changeDue, remainingShares } from '../../lib/split';

type Mode = 'full' | 'amount' | 'split' | 'items';
/** Ödenecek şey: tutar ya da kalemler. */
type Target = { amountKurus: number; itemIds?: number[]; expectedRemainingKurus: number };

interface Props {
  table: TableDetail;
  disabled: boolean;
  onPaid: (result: PayResult) => void;
  onClose: () => void;
}

const modes: readonly (readonly [Mode, string])[] = [
  ['full', 'Tamamı'],
  ['amount', 'Tutar gir'],
  ['split', 'Kişi başı'],
  ['items', 'Ürün seç'],
];

export function PaymentDialog({ table, disabled, onPaid, onClose }: Props) {
  const [mode, setMode] = useState<Mode>('full');
  const [amountText, setAmountText] = useState('');
  const [people, setPeople] = useState(2);
  const [paidShares, setPaidShares] = useState(0);
  const [selected, setSelected] = useState<number[]>([]);
  const [cashFor, setCashFor] = useState<Target | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [closed, setClosed] = useState<{ orderId: number; change: number } | null>(null);

  // isPending güncellenmeden önceki kısa aralıkta çift dokunuşu engeller.
  const inFlight = useRef(false);

  const qc = useQueryClient();
  const pay = useMutation({
    mutationFn: (v: { body: PayBody; change: number; mode: Mode }) => ordersApi.pay(table.id, v.body),
    // Para işlemi: asla otomatik yeniden denenmez (çift tahsilat riski) ve
    // çevrimdışıyken kuyrukta bekleyip sonradan kendiliğinden gitmez; hata olarak görünür.
    retry: false,
    networkMode: 'always',
    onSettled: () => { inFlight.current = false; },
    onSuccess: (r, v) => {
      setError(null);
      setCashFor(null);
      setSelected([]);
      setAmountText('');
      if (v.mode === 'split') setPaidShares((n) => n + 1);
      onPaid(r);
      if (r.closed) setClosed({ orderId: r.orderId, change: v.change });
    },
    onError: (e) => {
      setError(e.message);
      // Hesap değişmiş ya da yanıt kaybolmuş olabilir: güncel kalan tutarı göster.
      void qc.invalidateQueries({ queryKey: ['table', table.id] });
    },
  });

  // Hesap başka cihazda kapandıysa diyalog sessizce kaybolmasın, kapansın.
  useEffect(() => {
    if (!table.order && !closed && !pay.isPending) onClose();
  }, [table.order, closed, pay.isPending, onClose]);

  if (closed) return <ClosedSummary orderId={closed.orderId} change={closed.change} onClose={onClose} />;

  const order = table.order;
  if (!order) return null;
  const remaining = order.remainingKurus;
  const busy = disabled || pay.isPending;
  // Bekleyen ödeme varken diyalog kapanmasın: yeniden açılıp ikinci istek gitmesin.
  const safeClose = () => { if (!pay.isPending) onClose(); };

  const send = (method: 'Cash' | 'Card', t: Target, change = 0) => {
    if (inFlight.current || busy) return;
    inFlight.current = true;
    pay.mutate({
      body: {
        method,
        ...(t.itemIds ? { itemIds: t.itemIds } : { amountKurus: t.amountKurus }),
        expectedRemainingKurus: t.expectedRemainingKurus,
      },
      change,
      mode,
    });
  };

  let target: Target | null = null;
  let body: ReactNode = null;

  if (mode === 'full') {
    target = { amountKurus: remaining, expectedRemainingKurus: remaining };
    body = <p className="pay-remain"><span className="k">Kalan hesap</span><span className="v">{formatKurus(remaining)}</span></p>;
  } else if (mode === 'amount') {
    const k = parseTlToKurus(amountText);
    if (k !== null && k > 0 && k <= remaining) target = { amountKurus: k, expectedRemainingKurus: remaining };
    body = (
      <div>
        <label className="panel amount-in">
          <span aria-hidden="true">₺</span>
          <input inputMode="decimal" value={amountText} onChange={(e) => setAmountText(e.target.value)} autoFocus placeholder="0,00" aria-label="Alınacak tutar (₺)" />
        </label>
        {k !== null && k > remaining
          ? <p className="field-hint error">Tutar kalan hesaptan fazla.</p>
          : <p className="field-hint">Kalan {formatKurus(remaining)}</p>}
      </div>
    );
  } else if (mode === 'split') {
    const shares = remainingShares(remaining, people, paidShares);
    const left = shares.length;
    target = shares.length ? { amountKurus: shares[0], expectedRemainingKurus: remaining } : null;
    body = (
      <div className="panel split">
        <div className="panel-row">
          <span className="k">Kişi sayısı</span>
          <span className="stepper-wide">
            <button type="button" aria-label="Azalt" disabled={people <= Math.max(2, paidShares + 1)} onClick={() => setPeople((p) => p - 1)}>−</button>
            <strong>{people}</strong>
            <button type="button" aria-label="Artır" disabled={people >= 50} onClick={() => setPeople((p) => p + 1)}>+</button>
          </span>
        </div>
        <div className="panel-row">
          <span className="k">{paidShares} kişi ödedi, {left} kişi kaldı</span>
          <span className="v">{shares.length ? formatKurus(shares[0]) : '—'}</span>
        </div>
        {shares.length > 1 && (
          <div className="panel-row">
            <ol className="shares" aria-label="Kalan paylar">
              {shares.map((s, i) => <li key={i} className={i === 0 ? 'next' : ''}>{formatKurus(s)}</li>)}
            </ol>
          </div>
        )}
      </div>
    );
  } else {
    const unpaid = order.items.filter((i) => !i.isPaid);
    const picked = unpaid.filter((i) => selected.includes(i.id));
    const sum = picked.reduce((s, i) => s + i.lineTotalKurus, 0);
    if (picked.length) target = { amountKurus: sum, itemIds: picked.map((i) => i.id), expectedRemainingKurus: remaining };
    body = (
      <>
        <ul className="pick-items">
          {unpaid.map((i) => (
            <li key={i.id}>
              <label>
                <input type="checkbox" checked={selected.includes(i.id)}
                  onChange={(e) => setSelected((s) => (e.target.checked ? [...s, i.id] : s.filter((x) => x !== i.id)))} />
                <span className="pick-name">{i.productName} · {formatQuantity(i.unitType, i.quantity)}</span>
                <strong>{formatKurus(i.lineTotalKurus)}</strong>
              </label>
            </li>
          ))}
        </ul>
        <p className="pay-remain"><span className="k">Seçilen</span><span className="v">{formatKurus(sum)}</span></p>
      </>
    );
  }

  return (
    <Modal title={`${table.name} · Ödeme`} onClose={safeClose} onBack={cashFor && !busy ? () => { setCashFor(null); setError(null); } : undefined}>
      {cashFor ? (
        <CashStep target={cashFor} busy={busy} onConfirm={(change) => send('Cash', cashFor, change)} />
      ) : (
        <>
          <Segmented label="Ödeme şekli" value={mode} options={modes} disabled={busy} onChange={(m) => { setMode(m); setError(null); }} />
          {body}
          <div className="pay-buttons">
            <button className="primary" disabled={!target || busy} onClick={() => { setError(null); setCashFor(target); }}>
              Nakit{target ? ` · ${formatKurus(target.amountKurus)}` : ''}
            </button>
            <button className="tinted" disabled={!target || busy} onClick={() => target && send('Card', target)}>
              Kart{target ? ` · ${formatKurus(target.amountKurus)}` : ''}
            </button>
          </div>
        </>
      )}
      {error && <p className="error" role="alert">{error}</p>}
    </Modal>
  );
}

function CashStep({ target, busy, onConfirm }: {
  target: Target;
  busy: boolean;
  onConfirm: (change: number) => void;
}) {
  const [givenText, setGivenText] = useState('');
  const given = parseTlToKurus(givenText);
  const change = given !== null ? changeDue(given, target.amountKurus) : 0;
  const short = given !== null && given < target.amountKurus;

  return (
    <>
      <p className="pay-remain"><span className="k">Alınacak nakit</span><span className="v">{formatKurus(target.amountKurus)}</span></p>
      <div className="quick">
        {cashSuggestions(target.amountKurus).map((v) => (
          <button key={v} type="button" className={given === v ? 'on' : undefined} onClick={() => setGivenText((v / 100).toFixed(2).replace('.', ','))}>{formatKurus(v)}</button>
        ))}
      </div>
      <div>
        <label className="panel amount-in">
          <span aria-hidden="true">₺</span>
          <input inputMode="decimal" value={givenText} onChange={(e) => setGivenText(e.target.value)} placeholder="Müşterinin verdiği" aria-label="Müşterinin verdiği (isteğe bağlı)" />
        </label>
        {short ? <p className="field-hint error">Verilen para tutardan az.</p> : <p className="field-hint">İsteğe bağlı; para üstünü hesaplar.</p>}
      </div>
      {change > 0 && <p className="change-big"><span className="k">Para üstü</span><span className="v">{formatKurus(change)}</span></p>}
      <button type="button" className="primary wide" disabled={busy || short} onClick={() => onConfirm(change)}>Nakit ödemeyi kaydet</button>
    </>
  );
}

function ClosedSummary({ orderId, change, onClose }: { orderId: number; change: number; onClose: () => void }) {
  const navigate = useNavigate();
  const order = useQuery({ queryKey: ['order', orderId], queryFn: () => ordersApi.order(orderId) });
  return (
    <Modal title="Hesap kapandı" onClose={onClose}>
      <div className="closed-summary">
        <svg className="done-mark" viewBox="0 0 76 76" aria-hidden="true"><circle cx="38" cy="38" r="38" /><path d="M23 39l10 10 20-22" fill="none" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" /></svg>
        <h3>Hesap kapandı</h3>
        {order.data && (
          <>
            <p>Toplam <strong>{formatKurus(order.data.totalKurus)}</strong></p>
            <ul className="payments">
              {order.data.payments.map((p) => (
                <li key={p.id}>{p.method === 'Cash' ? 'Nakit' : 'Kart'} · {formatKurus(p.amountKurus)}</li>
              ))}
            </ul>
          </>
        )}
        {change > 0 && <p className="change-big"><span className="k">Para üstü</span><span className="v">{formatKurus(change)}</span></p>}
        <button className="primary wide" onClick={() => navigate('/')}>Masalara dön</button>
        <button className="wide" onClick={onClose}>Bu masada kal</button>
      </div>
    </Modal>
  );
}
