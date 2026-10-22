import { useEffect, useRef, useState } from 'react';
import { Modal } from '../../components/Modal';
import { formatKurus, formatPrice, formatQuantity } from '../../lib/format';
import { pressKey, shouldSubmitOnEnter } from '../../lib/keypad';
import { lineTotal } from '../../lib/pricing';

interface Props {
  title: string;
  pricePerKg: number;
  /** Düzeltmede mevcut gram. */
  initial?: number;
  busy: boolean;
  error: string | null;
  onSubmit: (grams: number) => void;
  onClose: () => void;
}

const digits = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];
const quick = [250, 500, 1000];

export function GramKeypad({ title, pricePerKg, initial, busy, error, onSubmit: submit, onClose }: Props) {
  const [value, setValue] = useState(initial ? String(initial) : '');
  const grams = Number(value) || 0;
  const valid = grams >= 1 && grams <= 99_999;

  // Aynı anda gelen iki Enter/tıklama tek kalem eklesin: istek bitene (busy true→false) dek kilit.
  const sent = useRef(false);
  const rootRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!busy) sent.current = false;
  }, [busy]);
  const onSubmit = (g: number) => {
    if (sent.current) return;
    sent.current = true;
    submit(g);
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      if (/^\d$/.test(e.key)) setValue((v) => pressKey(v, e.key));
      else if (e.key === 'Backspace') setValue((v) => pressKey(v, 'back'));
      // Tuş takımının kendi odaktaki düğmesinde Enter zaten tıklama üretir; çift ekleme olmasın.
      // Arkadaki ürün kartı odaktaysa Enter yine ekler (kartın yerel tıklaması engellenir).
      else if (e.key === 'Enter' && valid && !busy && shouldSubmitOnEnter(e.target as Element, rootRef.current?.closest('[role="dialog"]') ?? null)) {
        e.preventDefault();
        onSubmit(grams);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [valid, busy, grams, submit]);

  return (
    <Modal title={title} onClose={onClose}>
      <div ref={rootRef} className="readout">
        <div className="big">{grams.toLocaleString('tr-TR')}<span className="u">g</span></div>
        <div className="sub">{formatPrice('Gram', pricePerKg)}</div>
        <div className="line">{formatKurus(lineTotal('Gram', pricePerKg, grams))}</div>
      </div>
      <div className="quick">
        {quick.map((g) => (
          <button key={g} type="button" className={grams === g ? 'on' : undefined} onClick={() => setValue(String(g))}>
            {formatQuantity('Gram', g)}
          </button>
        ))}
      </div>
      <div className="keypad">
        {digits.map((k) => (
          <button key={k} type="button" className="key" onClick={() => setValue((v) => pressKey(v, k))}>{k}</button>
        ))}
        <button type="button" className="key fn" onClick={() => setValue('')}>Temizle</button>
        <button type="button" className="key" onClick={() => setValue((v) => pressKey(v, '0'))}>0</button>
        <button type="button" className="key fn" onClick={() => setValue((v) => pressKey(v, 'back'))} aria-label="Son rakamı sil">
          <svg viewBox="0 0 26 20" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" strokeLinecap="round" aria-hidden="true"><path d="M8 2h15a1.5 1.5 0 0 1 1.5 1.5v13A1.5 1.5 0 0 1 23 18H8l-6.5-8z" /><path d="m12 7 6 6M18 7l-6 6" /></svg>
        </button>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <button className="primary wide" disabled={!valid || busy} onClick={() => onSubmit(grams)}>
        {!valid ? 'Gram girin' : `${initial ? 'Güncelle' : 'Ekle'} · ${formatQuantity('Gram', grams)}`}
      </button>
    </Modal>
  );
}
