import { useEffect, useRef, useState } from 'react';
import { Modal } from '../../components/Modal';
import { formatKurus, formatPrice } from '../../lib/format';
import { pressKey } from '../../lib/keypad';
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
      if (/^\d$/.test(e.key)) setValue((v) => pressKey(v, e.key));
      else if (e.key === 'Backspace') setValue((v) => pressKey(v, 'back'));
      // Odaktaki düğmede Enter zaten tıklama üretir; çift ekleme olmasın.
      else if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement) && valid && !busy) onSubmit(grams);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [valid, busy, grams, submit]);

  return (
    <Modal title={title} onClose={onClose}>
      <div className="gram-display">
        <span className="gram-value">{value || '0'} g</span>
        <span className="muted">{formatPrice('Gram', pricePerKg)}</span>
        <strong className="gram-total">{formatKurus(lineTotal('Gram', pricePerKg, grams))}</strong>
      </div>
      <div className="quick">
        {quick.map((g) => (
          <button key={g} type="button" onClick={() => setValue(String(g))}>{g === 1000 ? '1 kg' : `${g} g`}</button>
        ))}
      </div>
      <div className="pinpad">
        {digits.map((k) => (
          <button key={k} type="button" onClick={() => setValue((v) => pressKey(v, k))}>{k}</button>
        ))}
        <button type="button" onClick={() => setValue('')}>C</button>
        <button type="button" onClick={() => setValue((v) => pressKey(v, '0'))}>0</button>
        <button type="button" onClick={() => setValue((v) => pressKey(v, 'back'))} aria-label="Sil">⌫</button>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <button className="primary wide" disabled={!valid || busy} onClick={() => onSubmit(grams)}>
        {initial ? 'Güncelle' : 'Ekle'}
      </button>
    </Modal>
  );
}
