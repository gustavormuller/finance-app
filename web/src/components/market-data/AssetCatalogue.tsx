import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, type MarketAsset, type MarketAssetInput } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { marketAssetClassLabels, providerKindLabels } from '@/lib/labels';
import { useOpenForm } from '@/lib/openForm';
import { refusal } from '@/lib/refusal';

import EditSource from './EditSource';
import { REGISTRATION_FIELDS, readRegistration } from './registration';
import RegistrationFields from './RegistrationFields';

/**
 * The shared catalogue: search it, register a ticker into it, and move an entry to another
 * provider or symbol (025). A 400 is shown under the fields it names and a 409 as the
 * API's sentence.
 */
export default function AssetCatalogue() {
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  // A new key remounts the registration fields blank, suggestions and all.
  const [registration, setRegistration] = useState(0);
  const editors = useOpenForm<MarketAsset>();
  const editing = editors.open;

  const assets = useQuery({ queryKey: ['market-assets', q], queryFn: () => api.searchMarketAssets(q) });

  const register = useMutation({
    mutationFn: (input: MarketAssetInput) => api.registerMarketAsset(input),
    onSuccess: async () => {
      setFailure(null);
      setFieldErrors({});
      setRegistration((key) => key + 1);
      await queryClient.invalidateQueries({ queryKey: ['market-assets'] });
    },
    onError: (error: Error) => {
      const refused = refusal(error, REGISTRATION_FIELDS);

      setFieldErrors(refused.fields);
      setFailure(refused.message);
    },
  });

  return (
    <section aria-labelledby="catalogue-heading" className="grid gap-4">
      <SectionHeading id="catalogue-heading">Ativos</SectionHeading>

      <form
        aria-label="Cadastrar ativo"
        className="glass grid gap-4 rounded-2xl p-5 sm:grid-cols-3 sm:p-6"
        onSubmit={(event) => {
          event.preventDefault();
          register.mutate(readRegistration(event.currentTarget));
        }}
      >
        <RegistrationFields key={registration} idPrefix="asset" errors={fieldErrors} />
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

      {editing && (
        <EditSource
          key={editing.of.id}
          asset={editing.of}
          // Also called by a save landing after another entry's Editar, whose editor stays.
          onDone={() => {
            if (editors.current() === editing) {
              editors.close();
            }
          }}
        />
      )}

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
              <TableHead>
                <span className="sr-only">Ações</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(assets.data ?? []).map((asset) => (
              <TableRow key={asset.id} data-testid={`market-asset-${asset.id}`}>
                <TableCell className="font-medium">{asset.ticker}</TableCell>
                <TableCell className="whitespace-normal">
                  {asset.name}
                  {asset.historyLoadedAt === null && (
                    <span className="text-muted-foreground block text-xs">Histórico completo na próxima sincronização.</span>
                  )}
                </TableCell>
                <TableCell className="hidden sm:table-cell">{marketAssetClassLabels[asset.class]}</TableCell>
                <TableCell className="hidden sm:table-cell">
                  {providerKindLabels[asset.provider]} <span className="text-muted-foreground">({asset.providerSymbol})</span>
                </TableCell>
                <TableCell className="hidden sm:table-cell">{asset.currency}</TableCell>
                <TableCell className="text-right">
                  <Button size="sm" variant="outline" aria-label={`Editar ${asset.ticker}`} onClick={() => editors.show(asset)}>
                    Editar
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
