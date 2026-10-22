import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

interface Props {
  title: string;
  onClose: () => void;
  /** Verilirse başlık yerine sol üstte "Geri" görünür (çok adımlı akış). */
  onBack?: () => void;
  children: ReactNode;
}

const phone = () => window.matchMedia('(max-width: 760px)').matches;
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
/** Sınırın ötesine çekişte direnç: çektikçe daha az izler. */
const rubber = (over: number, dim: number) => (over * dim * 0.55) / (dim + 0.55 * Math.abs(over));

/**
 * Telefonda alttan açılan sayfa (tutamaktan aşağı çekince kapanır), geniş ekranda ortada pencere.
 * Escape anında kapatır; dokunarak kapatma kısa bir çıkış hareketiyle olur.
 */
export function Modal({ title, onClose, onBack, children }: Props) {
  const downOnBackdrop = useRef(false);
  const sheetRef = useRef<HTMLDivElement>(null);
  const [closing, setClosing] = useState(false);
  const drag = useRef<{ id: number; y0: number; y: number; hist: { y: number; t: number }[] } | null>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onCloseRef.current();
    window.addEventListener('keydown', onKey);
    // İç içe sayfalarda arka plan kaydırması son sayfa kapanınca açılır.
    const html = document.documentElement;
    html.dataset.sheets = String(Number(html.dataset.sheets ?? 0) + 1);
    html.classList.add('sheet-open');
    return () => {
      window.removeEventListener('keydown', onKey);
      const left = Number(html.dataset.sheets ?? 1) - 1;
      if (left > 0) html.dataset.sheets = String(left);
      else { delete html.dataset.sheets; html.classList.remove('sheet-open'); }
    };
  }, []);

  /** Çıkış hareketinden sonra kapatır. Kapatma reddedilirse (ör. ödeme sürüyor) sayfa yerine döner. */
  const dismiss = () => {
    if (closing) return;
    setClosing(true);
    window.setTimeout(() => {
      onCloseRef.current();
      setClosing(false);
      if (sheetRef.current) sheetRef.current.style.transform = '';
    }, reducedMotion() ? 0 : 240);
  };

  const onPointerDown = (e: ReactPointerEvent<HTMLDivElement>) => {
    const t = e.target as Element;
    if (!phone() || closing || t.closest('button, input, a') || !t.closest('.sheet-grab, .sheet-head')) return;
    drag.current = { id: e.pointerId, y0: e.clientY, y: 0, hist: [{ y: e.clientY, t: e.timeStamp }] };
    e.currentTarget.setPointerCapture(e.pointerId);
    e.currentTarget.classList.add('dragging');
  };
  const onPointerMove = (e: ReactPointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || e.pointerId !== d.id) return;
    const dy = e.clientY - d.y0;
    d.y = dy < 0 ? -rubber(-dy, 200) : dy;
    d.hist.push({ y: e.clientY, t: e.timeStamp });
    if (d.hist.length > 5) d.hist.shift();
    e.currentTarget.style.transform = `translateY(${d.y}px)`;
  };
  const onPointerEnd = (e: ReactPointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || e.pointerId !== d.id) return;
    drag.current = null;
    const el = e.currentTarget;
    el.classList.remove('dragging');
    const a = d.hist[0], b = d.hist[d.hist.length - 1];
    const v = d.hist.length > 1 ? (b.y - a.y) / Math.max(1, b.t - a.t) : 0; // px/ms
    // Hızlı fiske ya da üçte birden fazla çekiş kapatır; yukarı fiske yerine döndürür.
    if (v > 0.5 || (v > -0.2 && d.y > el.offsetHeight * 0.3)) {
      el.style.transform = 'translateY(100%)';
      dismiss();
    } else {
      el.style.transform = '';
    }
  };

  return createPortal(
    <div
      className={`sheet-backdrop${closing ? ' closing' : ''}`}
      onPointerDown={(e) => { downOnBackdrop.current = e.target === e.currentTarget; }}
      onClick={(e) => { if (downOnBackdrop.current && e.target === e.currentTarget) dismiss(); }}
    >
      <div
        ref={sheetRef}
        className="sheet"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerEnd}
        onPointerCancel={onPointerEnd}
      >
        <div className="sheet-grab" aria-hidden="true"><span /></div>
        <div className="sheet-head">
          {onBack ? (
            <button type="button" className="sheet-back" onClick={onBack}>
              <svg viewBox="0 0 13 21" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M10.5 2.5 2.5 10.5l8 8" /></svg>
              Geri
            </button>
          ) : (
            <h2>{title}</h2>
          )}
          <button type="button" className="xbtn" onClick={dismiss} aria-label="Kapat">
            <svg viewBox="0 0 11 11" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M1 1l9 9M10 1l-9 9" /></svg>
          </button>
        </div>
        <div className="sheet-body">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
