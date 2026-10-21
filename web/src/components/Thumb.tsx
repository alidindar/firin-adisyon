export function Thumb({ url, large }: { url: string | null; large?: boolean }) {
  const cls = `thumb${large ? ' large' : ''}`;
  return url ? <img src={url} alt="" className={cls} loading="lazy" /> : <span className={`${cls} placeholder`} aria-hidden>🥐</span>;
}
