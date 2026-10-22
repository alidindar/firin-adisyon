/** Sayı tuş takımı girişini günceller. Baştaki sıfırı ve hane sınırını aşan rakamı yok sayar. */
export function pressKey(current: string, key: string, maxDigits = 5): string {
  if (key === 'back') return current.slice(0, -1);
  if (key === 'clear') return '';
  if (!/^\d$/.test(key)) return current;
  if (current === '' && key === '0') return current;
  if (current.length >= maxDigits) return current;
  return current + key;
}

/**
 * Enter ile gram girişi gönderilsin mi? Tuş takımının kendi odaktaki düğmesinde Enter zaten
 * yerel bir tıklama üretir; orada göndermeyiz (çift ekleme olmasın). Arkadaki ürün kartı gibi
 * tuş takımı dışındaki bir düğme odaktaysa Enter gönderir.
 */
export function shouldSubmitOnEnter(
  target: { tagName?: string } | null,
  keypad: { contains(node: never): boolean } | null,
): boolean {
  if (target?.tagName !== 'BUTTON') return true;
  return !keypad?.contains(target as never);
}
