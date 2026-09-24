import { describe, expect, it } from 'vitest';

import { movementTotal, parseDecimal, pointsDifference, rateUnits, toApiNumber } from './decimal';

describe('parseDecimal', () => {
  it('reads a comma or a dot as the decimal separator, and dots as grouping beside a comma', () => {
    expect(parseDecimal('32,12')).toBe(3_212_000_000n);
    expect(parseDecimal('32.12')).toBe(3_212_000_000n);
    expect(parseDecimal('1.234,5')).toBe(123_450_000_000n);
    expect(parseDecimal(' 0,00000001 ')).toBe(1n);
    expect(parseDecimal('-3')).toBe(-300_000_000n);
  });

  it('refuses what is not a number, or has more places than the column keeps', () => {
    expect(parseDecimal('abc')).toBeNull();
    expect(parseDecimal('1,2,3')).toBeNull();
    expect(parseDecimal('0,000000001')).toBeNull();
  });

  it('reads a blank field as nothing typed', () => {
    expect(parseDecimal('')).toBeNull();
    expect(parseDecimal('   ')).toBeNull();
  });
});

describe('toApiNumber', () => {
  it('turns what was typed into the number the API receives', () => {
    expect(toApiNumber('1.234,56')).toBe(1234.56);
    expect(toApiNumber('')).toBe(0);
  });
});

describe('movementTotal', () => {
  it('is quantity times price plus fees on a buy, exact to the cent', () => {
    // 0.1 × 3 is 0.30000000000000004 in floating point; here it is 0.30 exactly.
    expect(movementTotal('Buy', { quantity: '3', unitPrice: '0,1', amount: '', fees: '' })).toBe(30n);
    expect(movementTotal('Buy', { quantity: '100', unitPrice: '32,1234', amount: '', fees: '5' })).toBe(321_734n);
  });

  it('deducts fees from the proceeds of a sell', () => {
    expect(movementTotal('Sell', { quantity: '50', unitPrice: '40', amount: '', fees: '4,90' })).toBe(199_510n);
  });

  it('is the amount net of fees on income', () => {
    expect(movementTotal('Dividend', { quantity: '', unitPrice: '', amount: '120', fees: '' })).toBe(12_000n);
    expect(movementTotal('Jcp', { quantity: '', unitPrice: '', amount: '100', fees: '15' })).toBe(8_500n);
  });

  it('rounds a half cent to even, as the API does', () => {
    expect(movementTotal('Buy', { quantity: '1', unitPrice: '0,125', amount: '', fees: '' })).toBe(12n);
    expect(movementTotal('Buy', { quantity: '1', unitPrice: '0,135', amount: '', fees: '' })).toBe(14n);
  });

  it('has no total for a split, nor while a field cannot be read', () => {
    expect(movementTotal('Split', { quantity: '100', unitPrice: '', amount: '', fees: '' })).toBeNull();
    expect(movementTotal('Buy', { quantity: 'x', unitPrice: '10', amount: '', fees: '' })).toBeNull();
    expect(movementTotal('Buy', { quantity: '', unitPrice: '10', amount: '', fees: '' })).toBeNull();
  });
});

describe('rateUnits', () => {
  it('recovers the ten places the returns API writes, exactly', () => {
    expect(rateUnits(0.001500750125)).toBe(15_007_501n);
    expect(rateUnits(3.3454105367)).toBe(33_454_105_367n);
    expect(rateUnits(-0.0927)).toBe(-927_000_000n);
  });

  it('reads a rate float64 prints in exponent form', () => {
    expect(String(1.5e-7)).toBe('1.5e-7');
    expect(rateUnits(1.5e-7)).toBe(1_500n);
    expect(rateUnits(-1e-10)).toBe(-1n);
  });
});

describe('pointsDifference', () => {
  it('subtracts two rates exactly, in hundredths of a percentage point', () => {
    // In float64, 0.3 - 0.1 is 0.19999999999999998.
    expect(pointsDifference(0.3, 0.1)).toBe(2_000n);
    // 0.15007...% - 9.27% = -9.1199249875 p.p.
    expect(pointsDifference(0.001500750125, 0.0927)).toBe(-912n);
    expect(pointsDifference(0.0927, 0.0927)).toBe(0n);
  });

  it('rounds half to even, as the API does', () => {
    // A hundredth of a point is 1e-4 of a rate, so 5e-5 is exactly half of one.
    expect(pointsDifference(0.00005, 0)).toBe(0n);
    expect(pointsDifference(0.00015, 0)).toBe(2n);
    expect(pointsDifference(0, 0.00015)).toBe(-2n);
    expect(pointsDifference(0.00025, 0)).toBe(2n);
  });
});
