import { describe, expect, it } from 'vitest';
import { isAuthError } from './authError';

describe('isAuthError', () => {
  it('401 durum kodlu hatayı tanır', () => {
    expect(isAuthError(Object.assign(new Error('Unauthorized'), { statusCode: 401 }))).toBe(true);
  });
  it.each([
    [Object.assign(new Error('x'), { statusCode: 500 })],
    [new Error('Failed to fetch')],
    [null],
    [undefined],
    ['401'],
  ])('%s oturum hatası sayılmaz', (err) => {
    expect(isAuthError(err)).toBe(false);
  });
});
