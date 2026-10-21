import { describe, expect, it } from 'vitest';
import { fitWithin } from './image';

describe('fitWithin', () => {
  it('uzun kenarı sınıra indirir, oranı korur', () => {
    expect(fitWithin(4000, 3000, 800)).toEqual({ width: 800, height: 600 });
    expect(fitWithin(3000, 4000, 800)).toEqual({ width: 600, height: 800 });
  });

  it('küçük fotoğrafı büyütmez', () => {
    expect(fitWithin(640, 480, 800)).toEqual({ width: 640, height: 480 });
  });
});
