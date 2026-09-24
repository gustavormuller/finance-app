import { describe, expect, it } from 'vitest';
import { syncProviderLabel } from './labels';

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
