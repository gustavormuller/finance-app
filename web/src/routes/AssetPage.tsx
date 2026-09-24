import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from '@tanstack/react-router';

import { api, type Position } from '@/api/finance';
import Alert from '@/components/Alert';
import Movements from '@/components/investments/Movements';
import { INVESTMENTS, useDaily, usePositions } from '@/components/investments/queries';
import ValueChart from '@/components/investments/ValueChart';
import { Button } from '@/components/ui/button';
import { formatDate, marketAssetClassLabels } from '@/lib/labels';
import { formatMoney, formatPercent, formatQuantity, formatSignedMoney, formatUnitPrice } from '@/lib/money';

/**
 * `/investments/{id}` (007): one asset's position, its movements and its value over
 * time. The asset is read from the positions list, which holds everything shown here.
 */
export default function AssetPage() {
  const { assetId } = useParams({ from: '/protected/investments/$assetId' });
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const positions = usePositions();
  const position = positions.data?.find((candidate) => candidate.assetId === assetId);

  const remove = useMutation({
    mutationFn: () => api.removeAsset(assetId),
    onSuccess: async () => {
      await navigate({ to: '/investments' });
      await queryClient.invalidateQueries({ queryKey: INVESTMENTS });
    },
  });

  return (
    <section className="mx-auto grid max-w-5xl gap-10 px-4 py-8">
      <div>
        <Link to="/investments" className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← Investimentos
        </Link>
        <div className="mt-2 flex flex-wrap items-start justify-between gap-4">
          <div>
            <h2 className="text-2xl font-semibold tracking-tight">{position?.ticker ?? 'Ativo'}</h2>
            {position && (
              <p className="text-muted-foreground mt-1 text-sm">
                {position.nickname ?? position.name} · {marketAssetClassLabels[position.class]} · {position.currency}
              </p>
            )}
          </div>
          {position && (
            <Button variant="outline" disabled={remove.isPending} onClick={() => remove.mutate()}>
              Remover ativo
            </Button>
          )}
        </div>
      </div>

      {remove.isError && <Alert>{remove.error.message}</Alert>}
      {positions.isError && <Alert>Não foi possível carregar o ativo.</Alert>}
      {positions.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}
      {positions.data && !position && <p className="text-muted-foreground text-sm">Ativo não encontrado.</p>}

      {position && (
        <>
          <Summary position={position} />
          <History assetId={assetId} />
          <Movements assetId={assetId} currency={position.currency} />
        </>
      )}
    </section>
  );
}

/** The daily series; refreshed with the rest of `['investments']` after every write. */
function History({ assetId }: { assetId: string }) {
  const daily = useDaily(assetId);

  if (daily.isError) {
    return <Alert>Não foi possível carregar o histórico de valor.</Alert>;
  }

  return daily.data ? <ValueChart rows={daily.data} /> : null;
}

function Summary({ position }: { position: Position }) {
  const brl = (value: number | null) => (value === null ? '—' : formatMoney(value));
  const items: [string, string][] = [
    ['Quantidade', formatQuantity(position.quantity)],
    ['Preço médio', formatUnitPrice(position.averageCost, position.currency)],
    [
      'Cotação',
      position.price === null || position.priceDate === null
        ? 'Sem cotação'
        : `${formatUnitPrice(position.price, position.currency)} em ${formatDate(position.priceDate)}`,
    ],
    ['Valor', brl(position.valueBrl)],
    [
      'Resultado',
      position.unrealisedBrl === null
        ? '—'
        : `${formatSignedMoney(position.unrealisedBrl)}${position.unrealisedPct === null ? '' : ` (${formatPercent(position.unrealisedPct)})`}`,
    ],
    ['Resultado realizado', position.realisedBrl === null ? '—' : formatSignedMoney(position.realisedBrl)],
    ['Proventos', brl(position.dividendsBrl)],
  ];

  return (
    <dl data-testid="asset-summary" className="grid grid-cols-2 gap-4 sm:grid-cols-4">
      {items.map(([label, value]) => (
        <div key={label}>
          <dt className="text-muted-foreground text-xs">{label}</dt>
          <dd className="font-medium tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
