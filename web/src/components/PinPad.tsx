import { useCallback, useEffect, useRef, useState } from 'react';

interface Props {
  onComplete: (pin: string) => void;
  disabled?: boolean;
  error?: string | null;
}

const keys = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];

/** 4 haneli PIN. Dokunmatik tuşlarla ya da klavyeden girilir; 4. hanede onComplete çağrılır. */
export function PinPad({ onComplete, disabled, error }: Props) {
  const pinRef = useRef('');
  const [length, setLength] = useState(0);

  const press = useCallback(
    (digit: string) => {
      if (disabled) return;
      const next = pinRef.current + digit;
      if (next.length >= 4) {
        pinRef.current = '';
        setLength(0);
        onComplete(next.slice(0, 4));
      } else {
        pinRef.current = next;
        setLength(next.length);
      }
    },
    [disabled, onComplete],
  );

  const back = useCallback(() => {
    pinRef.current = pinRef.current.slice(0, -1);
    setLength(pinRef.current.length);
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) press(e.key);
      else if (e.key === 'Backspace') back();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [press, back]);

  return (
    <div className="pin">
      {/* Hata gelince noktalar sallanır; key değişince hareket yeniden başlar. */}
      <div key={error ?? ''} className={`pin-dots${error ? ' shake' : ''}`} aria-label={`${length} hane girildi`}>
        {[0, 1, 2, 3].map((i) => (
          <span key={i} className={i < length ? 'filled' : ''} />
        ))}
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="pinpad">
        {keys.map((k) => (
          <button key={k} type="button" onClick={() => press(k)} disabled={disabled}>
            {k}
          </button>
        ))}
        <button type="button" className="zero" onClick={() => press('0')} disabled={disabled}>
          0
        </button>
        <button type="button" className="fn" onClick={back} disabled={disabled}>
          Sil
        </button>
      </div>
    </div>
  );
}
