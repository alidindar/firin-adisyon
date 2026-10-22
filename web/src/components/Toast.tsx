import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';

interface Props {
  message: string;
  actionLabel?: string;
  onAction?: () => void;
  /** Süre dolunca ya da eyleme basılınca çağrılır. */
  onDone: () => void;
  durationMs?: number;
}

/** Alttaki kısa bildirim; "Geri al" gibi tek bir eylem taşır. Yeni bildirim için `key` değiştirin. */
export function Toast({ message, actionLabel, onAction, onDone, durationMs = 6000 }: Props) {
  const done = useRef(onDone);
  done.current = onDone;
  useEffect(() => {
    const id = window.setTimeout(() => done.current(), durationMs);
    return () => window.clearTimeout(id);
  }, [durationMs]);

  return createPortal(
    <div className="toast" role="status" aria-live="polite">
      <span>{message}</span>
      {actionLabel && (
        <button type="button" onClick={() => { onAction?.(); done.current(); }}>{actionLabel}</button>
      )}
    </div>,
    document.body,
  );
}
