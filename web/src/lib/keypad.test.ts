import { describe, expect, it } from 'vitest';
import { pressKey, shouldSubmitOnEnter } from './keypad';

describe('pressKey', () => {
  it('rakam ekler', () => {
    expect(pressKey('25', '0')).toBe('250');
  });
  it('baştaki sıfırı almaz', () => {
    expect(pressKey('', '0')).toBe('');
  });
  it('hane sınırını aşmaz', () => {
    expect(pressKey('99999', '1')).toBe('99999');
    expect(pressKey('99', '9', 2)).toBe('99');
  });
  it('siler ve temizler', () => {
    expect(pressKey('250', 'back')).toBe('25');
    expect(pressKey('', 'back')).toBe('');
    expect(pressKey('250', 'clear')).toBe('');
  });
  it('bilinmeyen tuşu yok sayar', () => {
    expect(pressKey('25', 'a')).toBe('25');
  });
});

describe('shouldSubmitOnEnter', () => {
  const inside = { tagName: 'BUTTON' };
  const outside = { tagName: 'BUTTON' };
  const keypad = { contains: (n: unknown) => n === inside };

  it('tuş takımının dışındaki düğmede (arkadaki ürün kartı) gönderir', () => {
    expect(shouldSubmitOnEnter(outside, keypad)).toBe(true);
  });
  it('tuş takımının kendi düğmesinde göndermez (yerel tıklama zaten gönderir)', () => {
    expect(shouldSubmitOnEnter(inside, keypad)).toBe(false);
  });
  it('düğme olmayan hedefte gönderir', () => {
    expect(shouldSubmitOnEnter({ tagName: 'BODY' }, keypad)).toBe(true);
    expect(shouldSubmitOnEnter(null, keypad)).toBe(true);
  });
  it('tuş takımı elemanı yoksa gönderir', () => {
    expect(shouldSubmitOnEnter(inside, null)).toBe(true);
  });
});
