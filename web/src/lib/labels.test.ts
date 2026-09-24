import { describe, expect, it } from 'vitest';
import { movementKindLabels, movementKinds, syncProviderLabel } from './labels';

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
