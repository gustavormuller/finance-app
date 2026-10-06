import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, type MarketAsset, type ProviderKind } from '@/api/finance';
import Alert from '@/components/Alert';
import FormField, { selectClasses } from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { providerKindCoverage, providerKindLabels, providerKinds } from '@/lib/labels';
import { refusal } from '@/lib/refusal';

import { suggestSymbol } from './suggestions';

const SOURCE_FIELDS = ['provider', 'providerSymbol'] as const;

/**
 * Moves a catalogue entry to another provider or symbol (025, decision 16): how an asset
 * registered on a provider that needs a key moves to Yahoo. Picking a provider suggests the
 * entry's symbol there until the person types one. The next sync replaces the entry's prices
 * with the new source's whole history.
 */
export default function EditSource({ asset, onDone }: { asset: MarketAsset; onDone: () => void }) {
  const queryClient = useQueryClient();
  const [provider, setProvider] = useState<ProviderKind>(asset.provider);
  const [typedSymbol, setTypedSymbol] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  const symbol =
    typedSymbol ?? (provider === asset.provider ? asset.providerSymbol : suggestSymbol(provider, asset.ticker, asset.class));

  const save = useMutation({
    mutationFn: () => api.updateMarketAssetSource(asset.id, { provider, providerSymbol: symbol }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['market-assets'] });
      onDone();
    },
    onError: (error: Error) => {
      const refused = refusal(error, SOURCE_FIELDS);

      setFieldErrors(refused.fields);
      setFailure(refused.message);
    },
  });

  return (
    <form
      aria-label="Editar fonte do ativo"
      className="glass grid gap-4 rounded-2xl p-5 sm:grid-cols-2 sm:p-6"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <div className="grid gap-1 sm:col-span-2">
        <h3 className="font-semibold">Editar fonte de {asset.ticker}</h3>
        <p className="text-muted-foreground text-sm">
          {`A próxima sincronização troca as cotações de ${asset.ticker} pelo histórico completo da nova fonte. `}
          {`A moeda (${asset.currency}) não muda.`}
        </p>
      </div>
      <FormField id="edit-provider" label="Provedor" errors={fieldErrors.provider}>
        <select
          id="edit-provider"
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
      <FormField id="edit-symbol" label="Símbolo no provedor" errors={fieldErrors.providerSymbol}>
        <Input
          id="edit-symbol"
          maxLength={50}
          required
          value={symbol}
          onChange={(event) => setTypedSymbol(event.target.value)}
        />
      </FormField>
      {failure && (
        <div className="sm:col-span-2">
          <Alert>{failure}</Alert>
        </div>
      )}
      <div className="flex gap-2 sm:col-span-2">
        <Button type="submit" disabled={save.isPending}>
          Salvar
        </Button>
        <Button type="button" variant="outline" onClick={onDone}>
          Cancelar
        </Button>
      </div>
    </form>
  );
}
