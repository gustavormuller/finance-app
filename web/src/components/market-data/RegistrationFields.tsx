import FormField, { selectClasses } from '@/components/FormField';
import { Input } from '@/components/ui/input';
import { marketAssetClasses, marketAssetClassLabels, providerKindLabels, providerKinds } from '@/lib/labels';

/**
 * A catalogue registration's fields: ticker, optional name, class, provider, symbol at
 * the provider and currency, each with the API's messages for it. Shared by
 * `/market-data` and the investments' add asset, which posts the same body.
 */
export default function RegistrationFields({ idPrefix, errors }: { idPrefix: string; errors: Record<string, string[]> }) {
  const id = (field: string) => `${idPrefix}-${field}`;

  return (
    <>
      <FormField id={id('ticker')} label="Ticker" errors={errors.ticker}>
        <Input id={id('ticker')} name="ticker" maxLength={20} className="uppercase" required />
      </FormField>
      <FormField id={id('name')} label="Nome (opcional)" errors={errors.name}>
        <Input id={id('name')} name="name" maxLength={200} />
      </FormField>
      <FormField id={id('class')} label="Classe" errors={errors.class}>
        <select id={id('class')} name="class" className={selectClasses} defaultValue="StockBr">
          {marketAssetClasses.map((value) => (
            <option key={value} value={value}>
              {marketAssetClassLabels[value]}
            </option>
          ))}
        </select>
      </FormField>
      <FormField id={id('provider')} label="Provedor" errors={errors.provider}>
        <select id={id('provider')} name="provider" className={selectClasses} defaultValue="Brapi">
          {providerKinds.map((value) => (
            <option key={value} value={value}>
              {providerKindLabels[value]}
            </option>
          ))}
        </select>
      </FormField>
      <FormField id={id('symbol')} label="Símbolo no provedor" errors={errors.providerSymbol}>
        <Input id={id('symbol')} name="providerSymbol" maxLength={50} placeholder="PETR4, bitcoin, AAPL" required />
      </FormField>
      <FormField id={id('currency')} label="Moeda" errors={errors.currency}>
        <select id={id('currency')} name="currency" className={selectClasses} defaultValue="BRL">
          <option value="BRL">BRL</option>
          <option value="USD">USD</option>
        </select>
      </FormField>
    </>
  );
}
