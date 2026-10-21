/** Tutarı kişi sayısına böler; kuruş artığı son kişiye eklenir. */
export function splitEqually(amount: number, people: number): number[] {
  if (people < 1 || amount <= 0) return [];
  const base = Math.floor(amount / people);
  const shares = Array<number>(people).fill(base);
  shares[people - 1] += amount - base * people;
  return shares;
}

export function changeDue(given: number, amount: number): number {
  return Math.max(0, given - amount);
}

/** 50, 100, 200, 500, 1000, 2000 ₺ */
const notes = [5000, 10000, 20000, 50000, 100000, 200000];

export function cashSuggestions(amount: number): number[] {
  const set = new Set<number>([amount]);
  for (const n of notes) {
    const v = Math.ceil(amount / n) * n;
    if (v > amount) set.add(v);
  }
  return [...set].sort((a, b) => a - b).slice(0, 4);
}

/** Kişi başı ödemede kalan payları verir; ilki sıradaki kişinindir. */
export function remainingShares(remaining: number, people: number, paidShares: number): number[] {
  return splitEqually(remaining, Math.max(people - paidShares, 1));
}
