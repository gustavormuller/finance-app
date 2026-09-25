import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from '@tanstack/react-router';

import { api, type Position } from '@/api/finance';
import Alert from '@/components/Alert';
import CurrencyNote from '@/components/investments/CurrencyNote';
import CurrencyToggle from '@/components/investments/CurrencyToggle';
import Movements from '@/components/investments/Movements';
import { INVESTMENTS, useDaily, usePositions } from '@/components/investments/queries';
import { useDisplayCurrency, type Display } from '@/components/investments/useDisplayCurrency';
import ValueChart from '@/components/investments/ValueChart';
import { Button } from '@/components/ui/button';
import { formatDate, marketAssetClassLabels } from '@/lib/labels';
import { formatPercent, formatQuantity, formatUnitPrice } from '@/lib/money';

/**
 * `/investments/{id}`: one asset's position, its movements and its value over time.
 * The asset is read from the positions list, which holds everything shown here.
 * Money is in the page's currency; prices and movements stay in the asset's own.
 */
export default function AssetPage() {
  const { assetId } = useParams({ from: '/protected/investments/$assetId' });
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const positions = usePositions();
  const position = positions.data?.find((candidate) => candidate.assetId === assetId);
  const display = useDisplayCurrency();

  const remove = useMutation({
    mutationFn: () => api.removeAsset(assetId),
    onSuccess: async () => {
      await navigate({ to: '/investments' });
      await queryClient.invalidateQueries({ queryKey: INVESTMENTS });
    },
  });

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <div>
        <Link to="/investments" className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← Investimentos
        </Link>
        <div className="mt-2 flex flex-wrap items-start justify-between gap-4">
          <div>
            <h2 className="text-3xl font-semibold tracking-tight">{position?.ticker ?? 'Ativo'}</h2>
            {position && (
              <p className="text-muted-foreground mt-1 text-sm">
                {position.nickname ?? position.name} · {marketAssetClassLabels[position.class]} · {position.currency}
              </p>
            )}
            {(display.fx || display.missingRate) && (
              <p className="text-muted-foreground mt-1 text-sm">
                <CurrencyNote display={display} />
              </p>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <CurrencyToggle />
            {position && (
              <Button variant="outline" disabled={remove.isPending} onClick={() => remove.mutate()}>
                Remover ativo
              </Button>
            )}
          </div>
        </div>
      </div>

      {remove.isError && <Alert>{remove.error.message}</Alert>}
      {positions.isError && <Alert>Não foi possível carregar o ativo.</Alert>}
      {positions.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}
      {positions.data && !position && <p className="text-muted-foreground text-sm">Ativo não encontrado.</p>}

      {position && (
        <>
          <Summary position={position} display={display} />
          <History assetId={assetId} inReais={display.currency === 'USD'} />
          <Movements assetId={assetId} currency={position.currency} />
        </>
      )}
    </section>
  );
}

/** The daily series; refreshed with the rest of `['investments']` after every write. */
function History({ assetId, inReais }: { assetId: string; inReais: boolean }) {
  const daily = useDaily(assetId);

  if (daily.isError) {
    return <Alert>Não foi possível carregar o histórico de valor.</Alert>;
  }

  return daily.data ? <ValueChart rows={daily.data} inReais={inReais} /> : null;
}

function Summary({ position, display }: { position: Position; display: Display }) {
  const money = (value: number | null) => (value === null ? '—' : display.money(value));
  const items: [string, string][] = [
    ['Quantidade', formatQuantity(position.quantity)],
    ['Preço médio', formatUnitPrice(position.averageCost, position.currency)],
    [
      'Cotação',
      position.price === null || position.priceDate === null
        ? 'Sem cotação'
        : `${formatUnitPrice(position.price, position.currency)} em ${formatDate(position.priceDate)}`,
    ],
    ['Valor', money(position.valueBrl)],
    [
      'Resultado',
      position.unrealisedBrl === null
        ? '—'
        : `${display.signedMoney(position.unrealisedBrl)}${position.unrealisedPct === null ? '' : ` (${formatPercent(position.unrealisedPct)})`}`,
    ],
    ['Resultado realizado', position.realisedBrl === null ? '—' : display.signedMoney(position.realisedBrl)],
    ['Proventos', money(position.dividendsBrl)],
  ];

  return (
    <dl data-testid="asset-summary" className="glass grid grid-cols-2 gap-4 rounded-2xl p-5 sm:grid-cols-4 sm:p-6">
      {items.map(([label, value]) => (
        <div key={label}>
          <dt className="text-muted-foreground text-xs">{label}</dt>
          <dd className="font-display text-lg font-semibold tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
