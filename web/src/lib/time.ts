/** Masanın açık kalma süresi: "şimdi", "12 dk", "1 sa 5 dk". */
export function elapsedLabel(fromIso: string, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - Date.parse(fromIso)) / 60_000));
  if (minutes < 1) return 'şimdi';
  if (minutes < 60) return `${minutes} dk`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? `${h} sa ${m} dk` : `${h} sa`;
}
