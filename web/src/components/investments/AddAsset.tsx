import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { api, ApiError, type AddAssetInput } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { REGISTRATION_FIELDS, readRegistration } from '@/components/market-data/registration';
import RegistrationFields from '@/components/market-data/RegistrationFields';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { marketAssetClassLabels } from '@/lib/labels';

import { INVESTMENTS } from './queries';

/**
 * Add asset (spec 007 UI): search 006's catalogue and add a result, or register a
 * ticker missing from it in the same call. Either way the new asset opens, since it
 * holds nothing until a first movement is recorded there.
 *
 * A 400 is shown under the registration field it names; a 409 ("already held", or
 * 006's duplicate symbol) and any other refusal as the API's sentence.
 */
export default function AddAsset() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  const [registering, setRegistering] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  const results = useQuery({
    queryKey: ['market-assets', q],
    queryFn: () => api.searchMarketAssets(q),
    enabled: q !== '',
  });

  const add = useMutation({
    mutationFn: (input: AddAssetInput) => api.addAsset(input),
    onSuccess: async (position) => {
      await queryClient.invalidateQueries({ queryKey: INVESTMENTS });
      await navigate({ to: '/investments/$assetId', params: { assetId: position.assetId } });
    },
    onError: (error: Error, input) => {
      // Only a registration has fields on screen to put a message under.
      const inline = !('marketAssetId' in input);
      const fields = error instanceof ApiError ? error.fields : {};
      const elsewhere = Object.entries(fields)
        .filter(([field]) => !inline || !(REGISTRATION_FIELDS as readonly string[]).includes(field))
        .flatMap(([, messages]) => messages);

      setFieldErrors(inline ? fields : {});
      setFailure(Object.keys(fields).length === 0 ? error.message : elsewhere.length > 0 ? elsewhere.join(' ') : null);
    },
  });

  return (
    <section aria-labelledby="add-asset-heading" className="glass grid gap-4 rounded-2xl p-5 sm:p-6">
      <SectionHeading id="add-asset-heading">Adicionar ativo</SectionHeading>

      <form
        role="search"
        className="flex items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          setQ(String(new FormData(event.currentTarget).get('q') ?? '').trim());
        }}
      >
        <div className="grid flex-1 gap-2">
          <Label htmlFor="add-asset-search">Buscar no catálogo</Label>
          <Input id="add-asset-search" name="q" placeholder="Ticker ou nome" />
        </div>
        <Button type="submit" variant="outline">
          Buscar
        </Button>
      </form>

      {failure && <Alert>{failure}</Alert>}
      {results.isError && <Alert>Não foi possível buscar no catálogo.</Alert>}

      {results.data?.length === 0 && (
        <p className="text-muted-foreground text-sm">Nenhum ativo corresponde a esta busca. Cadastre-o abaixo.</p>
      )}

      {results.data && results.data.length > 0 && (
        <ul className="bg-secondary rounded-xl px-4">
          {results.data.map((asset) => (
            <li key={asset.id} data-testid={`catalogue-result-${asset.id}`} className="border-border flex items-center justify-between gap-4 border-b py-2">
              <span>
                <span className="font-medium">{asset.ticker}</span> {asset.name}
                <span className="text-muted-foreground block text-xs">
                  {marketAssetClassLabels[asset.class]} · {asset.currency}
                </span>
              </span>
              <Button
                size="sm"
                variant="outline"
                disabled={add.isPending}
                onClick={() => add.mutate({ marketAssetId: asset.id })}
              >
                Adicionar
              </Button>
            </li>
          ))}
        </ul>
      )}

      {registering ? (
        <form
          aria-label="Cadastrar e adicionar ativo"
          className="grid gap-4 sm:grid-cols-3"
          onSubmit={(event) => {
            event.preventDefault();
            add.mutate(readRegistration(event.currentTarget));
          }}
        >
          <RegistrationFields idPrefix="new-asset" errors={fieldErrors} />
          <div className="flex gap-2 sm:col-span-3">
            <Button type="submit" disabled={add.isPending}>
              Cadastrar e adicionar
            </Button>
            <Button type="button" variant="outline" onClick={() => setRegistering(false)}>
              Cancelar
            </Button>
          </div>
        </form>
      ) : (
        <div>
          <Button type="button" variant="ghost" onClick={() => setRegistering(true)}>
            Cadastrar novo ativo
          </Button>
        </div>
      )}
    </section>
  );
}
