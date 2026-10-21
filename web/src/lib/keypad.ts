/** Sayı tuş takımı girişini günceller. Baştaki sıfırı ve hane sınırını aşan rakamı yok sayar. */
export function pressKey(current: string, key: string, maxDigits = 5): string {
  if (key === 'back') return current.slice(0, -1);
  if (key === 'clear') return '';
  if (!/^\d$/.test(key)) return current;
  if (current === '' && key === '0') return current;
  if (current.length >= maxDigits) return current;
  return current + key;
}
