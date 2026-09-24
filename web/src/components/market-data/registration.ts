import type { MarketAssetClass, MarketAssetInput, ProviderKind } from '@/api/finance';

/** The fields rendered, so a 400 naming another one is shown elsewhere. */
export const REGISTRATION_FIELDS = ['ticker', 'name', 'class', 'provider', 'providerSymbol', 'currency'] as const;

/** The registration body, read from a form holding {@link RegistrationFields}. */
export function readRegistration(form: HTMLFormElement): MarketAssetInput {
  const values = new FormData(form);
  const text = (key: string) => String(values.get(key) ?? '');

  return {
    ticker: text('ticker'),
    name: text('name'),
    class: text('class') as MarketAssetClass,
    provider: text('provider') as ProviderKind,
    providerSymbol: text('providerSymbol'),
    currency: text('currency'),
  };
}
