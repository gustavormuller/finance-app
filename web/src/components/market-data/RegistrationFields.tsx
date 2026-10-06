import { useState } from 'react';

import type { MarketAssetClass, ProviderKind } from '@/api/finance';
import FormField, { selectClasses } from '@/components/FormField';
import { Input } from '@/components/ui/input';
import {
  marketAssetClasses,
  marketAssetClassLabels,
  providerKindCoverage,
  providerKindLabels,
  providerKinds,
} from '@/lib/labels';

import { suggestCurrency, suggestSymbol } from './suggestions';

/**
 * A catalogue registration's fields: ticker, optional name, class, provider, symbol at
 * the provider and currency, each with the API's messages for it. Shared by
 * `/market-data` and the investments' add asset, which posts the same body and offers
 * only the classes a portfolio can hold (025).
 *
 * The symbol and the currency follow the ticker, class and provider (025, decision 18)
 * until the person changes that field; from then on it is theirs. A parent that wants the
 * fields blank again remounts them with a new `key`.
 */
export default function RegistrationFields({
  idPrefix,
  errors,
  classes = marketAssetClasses,
}: {
  idPrefix: string;
  errors: Record<string, string[]>;
  classes?: MarketAssetClass[];
}) {
  const id = (field: string) => `${idPrefix}-${field}`;
  const [ticker, setTicker] = useState('');
  const [assetClass, setAssetClass] = useState<MarketAssetClass>('StockBr');
  const [provider, setProvider] = useState<ProviderKind>('Yahoo');
  const [typedSymbol, setTypedSymbol] = useState<string | null>(null);
  const [chosenCurrency, setChosenCurrency] = useState<string | null>(null);

  const symbol = typedSymbol ?? suggestSymbol(provider, ticker, assetClass);
  const currency = chosenCurrency ?? suggestCurrency(provider, symbol, assetClass);

  return (
    <>
      <FormField id={id('ticker')} label="Ticker" errors={errors.ticker}>
        <Input
          id={id('ticker')}
          name="ticker"
          maxLength={20}
          className="uppercase"
          required
          value={ticker}
          onChange={(event) => setTicker(event.target.value)}
        />
      </FormField>
      <FormField id={id('name')} label="Nome (opcional)" errors={errors.name}>
        <Input id={id('name')} name="name" maxLength={200} />
      </FormField>
      <FormField id={id('class')} label="Classe" errors={errors.class}>
        <select
          id={id('class')}
          name="class"
          className={selectClasses}
          value={assetClass}
          onChange={(event) => setAssetClass(event.target.value as MarketAssetClass)}
        >
          {classes.map((value) => (
            <option key={value} value={value}>
              {marketAssetClassLabels[value]}
            </option>
          ))}
        </select>
      </FormField>
      <FormField id={id('provider')} label="Provedor" errors={errors.provider}>
        <select
          id={id('provider')}
          name="provider"
          className={selectClasses}
          value={provider}
          onChange={(event) => setProvider(event.target.value as ProviderKind)}
        >
          {providerKinds.map((value) => (
            <option key={value} value={value}>
              {providerKindLabels[value]} ({providerKindCoverage[value]})
            </option>
          ))}
        </select>
      </FormField>
      <FormField id={id('symbol')} label="Símbolo no provedor" errors={errors.providerSymbol}>
        <Input
          id={id('symbol')}
          name="providerSymbol"
          maxLength={50}
          placeholder="PETR4.SA, ^BVSP, BTC-USD, BRL=X"
          required
          value={symbol}
          onChange={(event) => setTypedSymbol(event.target.value)}
        />
      </FormField>
      <FormField id={id('currency')} label="Moeda" errors={errors.currency}>
        <select
          id={id('currency')}
          name="currency"
          className={selectClasses}
          value={currency}
          onChange={(event) => setChosenCurrency(event.target.value)}
        >
          <option value="BRL">BRL</option>
          <option value="USD">USD</option>
        </select>
      </FormField>
    </>
  );
}
