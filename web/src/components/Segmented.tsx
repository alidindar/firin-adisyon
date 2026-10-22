import type { ReactNode } from 'react';

interface Props<T extends string> {
  label: string;
  value: T;
  options: readonly (readonly [T, ReactNode])[];
  onChange: (value: T) => void;
  disabled?: boolean;
}

/** iOS bölümlü seçici: seçili bölümün arkasındaki kart kayarak yer değiştirir. */
export function Segmented<T extends string>({ label, value, options, onChange, disabled }: Props<T>) {
  const index = Math.max(0, options.findIndex(([v]) => v === value));
  return (
    <div className="seg" role="tablist" aria-label={label}>
      <span
        className="seg-thumb"
        aria-hidden="true"
        style={{ width: `calc(${100 / options.length}% - ${4 / options.length}px)`, transform: `translateX(${index * 100}%)` }}
      />
      {options.map(([v, content]) => (
        <button key={v} type="button" role="tab" aria-selected={v === value} disabled={disabled} onClick={() => onChange(v)}>
          {content}
        </button>
      ))}
    </div>
  );
}
