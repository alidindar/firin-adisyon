import { describe, expect, it } from 'vitest';
import { pressKey } from './keypad';

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
