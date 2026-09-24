import { describe, expect, it } from 'vitest';

import { NO_DATA, formatPoints, formatRate } from './rates';

describe('formatRate', () => {
  it('writes a fraction as a pt-BR percentage with its sign', () => {
    expect(formatRate(0.0927)).toBe('+9,27%');
    expect(formatRate(-0.01)).toBe('-1,00%');
    expect(formatRate(0)).toBe('0,00%');
    expect(formatRate(12.345)).toBe('+1.234,50%');
  });

  it('writes a missing rate as a placeholder, never NaN', () => {
    expect(formatRate(null)).toBe(NO_DATA);
    expect(formatRate(undefined)).toBe(NO_DATA);
    expect(NO_DATA).toBe('Sem dados');
  });
});

describe('formatPoints', () => {
  it('writes hundredths of a percentage point as p.p. in pt-BR', () => {
    expect(formatPoints(-912n)).toBe('-9,12 p.p.');
    expect(formatPoints(2_000n)).toBe('+20,00 p.p.');
    expect(formatPoints(0n)).toBe('0,00 p.p.');
    expect(formatPoints(123_456n)).toBe('+1.234,56 p.p.');
  });
});
