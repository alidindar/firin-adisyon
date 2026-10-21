import { describe, expect, it } from 'vitest';
import { elapsedLabel } from './time';

const opened = '2026-10-21T06:00:00Z';
const at = (minutes: number, seconds = 0) => new Date(Date.parse(opened) + minutes * 60_000 + seconds * 1000);

describe('elapsedLabel', () => {
  it.each([
    [at(0), 'şimdi'],
    [at(0, 59), 'şimdi'],
    [at(5), '5 dk'],
    [at(59), '59 dk'],
    [at(60), '1 sa'],
    [at(65), '1 sa 5 dk'],
    [at(-3), 'şimdi'],
  ])('%s → %s', (now, expected) => {
    expect(elapsedLabel(opened, now)).toBe(expected);
  });
});
