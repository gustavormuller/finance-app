import { describe, expect, it } from 'vitest';
import { benchmarkCodes, benchmarkLabel, movementKindLabels, movementKinds, returnsPeriodLabels, returnsPeriods, syncProviderLabel } from './labels';

describe('syncProviderLabel', () => {
  it('names every summary key in Portuguese, the snapshot rebuild included', () => {
    expect(syncProviderLabel('Brapi')).toBe('brapi');
    expect(syncProviderLabel('Bcb')).toBe('Banco Central (SGS)');
    expect(syncProviderLabel('Snapshots')).toBe('Posições da carteira');
  });

  it('shows an unknown key as sent', () => {
    expect(syncProviderLabel('Unknown')).toBe('Unknown');
  });
});

describe('movementKindLabels', () => {
  it('names every movement kind in Portuguese, in the order the form offers them', () => {
    expect(movementKinds.map((kind) => movementKindLabels[kind])).toEqual([
      'Compra',
      'Venda',
      'Dividendo',
      'JCP',
      'Desdobramento',
    ]);
  });
});

describe('benchmarkLabel', () => {
  it('names each of 008\'s benchmark codes as the spec does, in the order they are shown', () => {
    expect(benchmarkCodes.map(benchmarkLabel)).toEqual(['CDI', 'SELIC', 'IPCA + 6%', 'Dólar', 'S&P 500 (IVVB11)']);
  });

  it('shows an unknown code as sent', () => {
    expect(benchmarkLabel('IBOV')).toBe('IBOV');
  });
});

describe('returnsPeriodLabels', () => {
  it('names every period in Portuguese, in the order the selector offers them', () => {
    expect(returnsPeriods.map((period) => returnsPeriodLabels[period])).toEqual([
      'Desde o início',
      'No ano',
      '12 meses',
      'Personalizado',
    ]);
  });
});
