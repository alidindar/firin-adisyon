import { useEffect, useRef, type PointerEvent as ReactPointerEvent, type ReactNode } from 'react';
import type { OrderItem, TableDetail } from '../../api/orders';
import { formatKurus, formatPrice, formatQuantity } from '../../lib/format';

interface Props {
  table: TableDetail;
  disabled: boolean;
  onSetQuantity: (item: OrderItem, quantity: number) => void;
  onVoid: (item: OrderItem) => void;
  onEditGram: (item: OrderItem) => void;
  /** Boş hesapta "Ürün ekle" bağlantısı (telefonda Ürünler sekmesine geçer). */
  onAddProducts?: () => void;
  footer?: ReactNode;
}

export function OrderPanel({ table, disabled, onSetQuantity, onVoid, onEditGram, onAddProducts, footer }: Props) {
  const order = table.order;

  return (
    <div className="order-panel">
      {!order || order.items.length === 0 ? (
        <div className="empty-state">
          <strong>Hesap boş</strong>
          Ürün eklendikçe burada görünür.
          {onAddProducts && <><br /><button type="button" className="link" onClick={onAddProducts}>Ürün ekle</button></>}
        </div>
      ) : (
        <>
          <div className="group-title">Kalemler</div>
          <ul className="group order-lines">
            {order.items.map((i) => (
              <SwipeRow key={i.id} item={i} disabled={disabled} onVoid={() => onVoid(i)}>
                <div className="line-main">
                  <span className="line-name">
                    {i.productName}
                    {i.isPaid && <span className="tag">Ödendi</span>}
                  </span>
                  <span className="line-detail">{formatQuantity(i.unitType, i.quantity)} × {formatPrice(i.unitType, i.unitPriceKurus)}</span>
                </div>
                {i.isPaid ? (
                  <span />
                ) : i.unitType === 'Piece' ? (
                  <span className="stepper">
                    <button type="button" aria-label="Bir azalt" disabled={disabled || i.quantity <= 1} onClick={() => onSetQuantity(i, i.quantity - 1)}>−</button>
                    <i aria-hidden="true" />
                    <button type="button" aria-label="Bir artır" disabled={disabled || i.quantity >= 999} onClick={() => onSetQuantity(i, i.quantity + 1)}>+</button>
                  </span>
                ) : (
                  <button type="button" className="pill-btn" disabled={disabled} onClick={() => onEditGram(i)} aria-label={`${i.productName} gramını düzelt`}>Düzelt</button>
                )}
                <span className="line-total">{formatKurus(i.lineTotalKurus)}</span>
                {i.isPaid ? (
                  <span />
                ) : (
                  <button type="button" className="void-inline" disabled={disabled} onClick={() => onVoid(i)} aria-label={`${i.productName} kalemini iptal et`}>
                    <svg viewBox="0 0 11 11" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M1 1l9 9M10 1l-9 9" /></svg>
                  </button>
                )}
              </SwipeRow>
            ))}
          </ul>
          <p className="swipe-hint">İptal etmek için satırı sola çekin.</p>
        </>
      )}
      {order && (
        <>
          <div className="group-title">Hesap</div>
          <div className="group">
            <div className="sum-row"><span className="k">Toplam</span><span>{formatKurus(order.totalKurus)}</span></div>
            {order.paidKurus > 0 && (
              <>
                <div className="sum-row"><span className="k">Ödenen</span><span>−{formatKurus(order.paidKurus)}</span></div>
                <div className="sum-row big"><span>Kalan</span><span>{formatKurus(order.remainingKurus)}</span></div>
              </>
            )}
          </div>
        </>
      )}
      {footer}
    </div>
  );
}

const OPEN_X = -88;
/** Aynı anda tek satır açık kalır. */
let openRow: { close: () => void } | null = null;
const rubber = (over: number, dim: number) => (over * dim * 0.55) / (dim + 0.55 * Math.abs(over));

/**
 * Dokunmatikte satır sola çekilince arkadaki "İptal" görünür. Yön 10 px'te belirlenir; dikey hareket kaydırmaya kalır.
 * Bırakınca hıza göre açılır ya da kapanır. Fareyle sürükleme yok; orada satırdaki çarpı kullanılır.
 */
function SwipeRow({ item, disabled, onVoid, children }: { item: OrderItem; disabled: boolean; onVoid: () => void; children: ReactNode }) {
  const rowRef = useRef<HTMLDivElement>(null);
  const open = useRef(false);
  const drag = useRef<{ id: number; x0: number; y0: number; base: number; dir: 'x' | null; x: number; hist: { x: number; t: number }[] } | null>(null);
  const canSwipe = !item.isPaid && !disabled;

  const setX = (x: number, anim: boolean) => {
    const el = rowRef.current;
    if (!el) return;
    el.classList.toggle('anim', anim);
    el.style.transform = x ? `translateX(${x}px)` : '';
    const li = el.parentElement;
    if (!li) return;
    if (x) li.classList.add('reveal');
    // Kapanış hareketi bitince arkadaki düğme gizlenir.
    else if (anim) el.addEventListener('transitionend', () => { if (!el.style.transform) li.classList.remove('reveal'); }, { once: true });
    else li.classList.remove('reveal');
  };
  // Kimliği render'dan render'a aynı kalır; açık satır bununla karşılaştırılır.
  const handle = useRef({ close: () => {} }).current;
  const close = () => {
    open.current = false;
    setX(0, true);
    if (openRow === handle) openRow = null;
  };
  handle.close = close;

  // Açıkken başka yere dokunmak kapatır.
  useEffect(() => {
    const onDown = (e: PointerEvent) => {
      if (open.current && !rowRef.current?.parentElement?.contains(e.target as Node)) close();
    };
    document.addEventListener('pointerdown', onDown);
    return () => {
      document.removeEventListener('pointerdown', onDown);
      if (openRow === handle) openRow = null;
    };
  });
  useEffect(() => { if (!canSwipe && open.current) close(); });

  const onPointerDown = (e: ReactPointerEvent<HTMLDivElement>) => {
    if (!canSwipe || e.pointerType === 'mouse' || (e.target as Element).closest('button')) return;
    if (openRow && openRow !== handle) openRow.close();
    drag.current = { id: e.pointerId, x0: e.clientX, y0: e.clientY, base: open.current ? OPEN_X : 0, dir: null, x: 0, hist: [] };
  };
  const onPointerMove = (e: ReactPointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || e.pointerId !== d.id) return;
    const dx = e.clientX - d.x0, dy = e.clientY - d.y0;
    if (!d.dir) {
      if (Math.abs(dx) > 10 && Math.abs(dx) > Math.abs(dy)) {
        d.dir = 'x';
        e.currentTarget.setPointerCapture(e.pointerId);
      } else if (Math.abs(dy) > 10) {
        drag.current = null;
        return;
      } else return;
    }
    let x = d.base + dx;
    if (x > 0) x = rubber(x, 120);
    else if (x < OPEN_X) x = OPEN_X + rubber(x - OPEN_X, 120);
    d.x = x;
    d.hist.push({ x: e.clientX, t: e.timeStamp });
    if (d.hist.length > 5) d.hist.shift();
    setX(x, false);
  };
  const onPointerEnd = (e: ReactPointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || e.pointerId !== d.id) return;
    drag.current = null;
    if (d.dir !== 'x') return;
    const a = d.hist[0], b = d.hist[d.hist.length - 1];
    const v = d.hist.length > 1 ? (b.x - a.x) / Math.max(1, b.t - a.t) : 0;
    const shouldOpen = v < -0.3 || (v <= 0.3 && d.x < OPEN_X / 2);
    open.current = shouldOpen;
    setX(shouldOpen ? OPEN_X : 0, true);
    if (shouldOpen) openRow = handle;
    else if (openRow === handle) openRow = null;
  };

  return (
    <li className={item.isPaid ? 'paid' : undefined}>
      {canSwipe && (
        <button type="button" className="void-btn" tabIndex={-1} aria-hidden="true" onClick={() => { close(); onVoid(); }}>İptal</button>
      )}
      <div
        ref={rowRef}
        className="line-row"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerEnd}
        onPointerCancel={onPointerEnd}
      >
        {children}
      </div>
    </li>
  );
}
