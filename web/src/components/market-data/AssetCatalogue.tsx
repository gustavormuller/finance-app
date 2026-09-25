import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';

import { api, ApiError, type MarketAssetInput } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { marketAssetClassLabels, providerKindLabels } from '@/lib/labels';

import { REGISTRATION_FIELDS, readRegistration } from './registration';
import RegistrationFields from './RegistrationFields';

/**
 * The shared catalogue: search it, and register a ticker into it. A 400 is shown under
 * the fields it names and a 409 as the API's sentence.
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
        .filter(([field]) => !(REGISTRATION_FIELDS as readonly string[]).includes(field))
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
        className="glass grid gap-4 rounded-2xl p-5 sm:grid-cols-3 sm:p-6"
        onSubmit={(event) => {
          event.preventDefault();
          register.mutate(readRegistration(event.currentTarget));
        }}
      >
        <RegistrationFields idPrefix="asset" errors={fieldErrors} />
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
        <p className="text-muted-foreground glass rounded-2xl py-12 text-center text-sm">
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

