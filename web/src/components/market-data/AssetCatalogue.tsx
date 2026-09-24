import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';

import { api, ApiError, type MarketAssetClass, type MarketAssetInput, type ProviderKind } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { marketAssetClasses, marketAssetClassLabels, providerKindLabels, providerKinds } from '@/lib/labels';

const selectClasses =
  'border-input dark:bg-input/30 h-9 w-full rounded-md border bg-transparent px-3 py-1 ' +
  'text-base shadow-xs outline-none md:text-sm';

/** The fields the form renders, so a 400 naming one is shown under it. */
const FIELDS = ['ticker', 'name', 'class', 'provider', 'providerSymbol', 'currency'] as const;

/**
 * The shared catalogue (spec 006 UI): search it, and register a ticker into it. A 400
 * is shown under the fields it names and a 409 as the API's sentence.
 */
export default function AssetCatalogue() {
  const queryClient = useQueryClient();
  const form = useRef<HTMLFormElement>(null);
  const [q, setQ] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  const assets = useQuery({ queryKey: ['market-assets', q], queryFn: () => api.searchMarketAssets(q) });

  const register = useMutation({
    mutationFn: (input: MarketAssetInput) => api.registerMarketAsset(input),
    onSuccess: async () => {
      setFailure(null);
      setFieldErrors({});
      form.current?.reset();
      await queryClient.invalidateQueries({ queryKey: ['market-assets'] });
    },
    onError: (error: Error) => {
      const fields = error instanceof ApiError ? error.fields : {};
      const elsewhere = Object.entries(fields)
        .filter(([field]) => !(FIELDS as readonly string[]).includes(field))
        .flatMap(([, messages]) => messages);

      setFieldErrors(fields);
      setFailure(Object.keys(fields).length === 0 ? error.message : elsewhere.length > 0 ? elsewhere.join(' ') : null);
    },
  });

  return (
    <section aria-labelledby="catalogue-heading" className="grid gap-4">
      <SectionHeading id="catalogue-heading">Ativos</SectionHeading>

      <form
        ref={form}
        aria-label="Cadastrar ativo"
        className="grid gap-4 sm:grid-cols-3"
        onSubmit={(event) => {
          event.preventDefault();
          const values = new FormData(event.currentTarget);
          const text = (key: string) => String(values.get(key) ?? '');

          register.mutate({
            ticker: text('ticker'),
            name: text('name'),
            class: text('class') as MarketAssetClass,
            provider: text('provider') as ProviderKind,
            providerSymbol: text('providerSymbol'),
            currency: text('currency'),
          });
        }}
      >
        <Field id="asset-ticker" label="Ticker" errors={fieldErrors.ticker}>
          <Input id="asset-ticker" name="ticker" maxLength={20} className="uppercase" required />
        </Field>
        <Field id="asset-name" label="Nome (opcional)" errors={fieldErrors.name}>
          <Input id="asset-name" name="name" maxLength={200} />
        </Field>
        <Field id="asset-class" label="Classe" errors={fieldErrors.class}>
          <select id="asset-class" name="class" className={selectClasses} defaultValue="StockBr">
            {marketAssetClasses.map((value) => (
              <option key={value} value={value}>
                {marketAssetClassLabels[value]}
              </option>
            ))}
          </select>
        </Field>
        <Field id="asset-provider" label="Provedor" errors={fieldErrors.provider}>
          <select id="asset-provider" name="provider" className={selectClasses} defaultValue="Brapi">
            {providerKinds.map((value) => (
              <option key={value} value={value}>
                {providerKindLabels[value]}
              </option>
            ))}
          </select>
        </Field>
        <Field id="asset-symbol" label="Símbolo no provedor" errors={fieldErrors.providerSymbol}>
          <Input id="asset-symbol" name="providerSymbol" maxLength={50} placeholder="PETR4, bitcoin, AAPL" required />
        </Field>
        <Field id="asset-currency" label="Moeda" errors={fieldErrors.currency}>
          <select id="asset-currency" name="currency" className={selectClasses} defaultValue="BRL">
            <option value="BRL">BRL</option>
            <option value="USD">USD</option>
          </select>
        </Field>
        <div className="sm:col-span-3">
          <Button type="submit" disabled={register.isPending}>
            Cadastrar ativo
          </Button>
        </div>
      </form>

      {failure && <Alert>{failure}</Alert>}

      <form
        role="search"
        className="flex items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          setQ(String(new FormData(event.currentTarget).get('q') ?? '').trim());
        }}
      >
        <div className="grid flex-1 gap-2">
          <Label htmlFor="asset-search">Buscar ativo</Label>
          <Input id="asset-search" name="q" placeholder="Ticker ou nome" />
        </div>
        <Button type="submit" variant="outline">
          Buscar
        </Button>
      </form>

      {assets.isError && <Alert>Não foi possível carregar os ativos.</Alert>}

      {assets.data?.length === 0 ? (
        <p className="text-muted-foreground border-border border-t py-12 text-center text-sm">
          {q ? 'Nenhum ativo corresponde a esta busca.' : 'Nenhum ativo cadastrado ainda.'}
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Ticker</TableHead>
              <TableHead>Nome</TableHead>
              <TableHead className="hidden sm:table-cell">Classe</TableHead>
              <TableHead className="hidden sm:table-cell">Provedor</TableHead>
              <TableHead className="hidden sm:table-cell">Moeda</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(assets.data ?? []).map((asset) => (
              <TableRow key={asset.id} data-testid={`market-asset-${asset.id}`}>
                <TableCell className="font-medium">{asset.ticker}</TableCell>
                <TableCell className="whitespace-normal">{asset.name}</TableCell>
                <TableCell className="hidden sm:table-cell">{marketAssetClassLabels[asset.class]}</TableCell>
                <TableCell className="hidden sm:table-cell">
                  {providerKindLabels[asset.provider]} <span className="text-muted-foreground">({asset.providerSymbol})</span>
                </TableCell>
                <TableCell className="hidden sm:table-cell">{asset.currency}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}

function Field({
  id,
  label,
  errors,
  children,
}: {
  id: string;
  label: string;
  errors?: string[] | undefined;
  children: React.ReactNode;
}) {
  return (
    <div className="grid content-start gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {errors && errors.length > 0 && (
        <p role="alert" className="text-destructive text-sm">
          {errors.join(' ')}
        </p>
      )}
    </div>
  );
}
