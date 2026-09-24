import { useQueries } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { api, type AssetReturns, type Position, type ReturnsQuery } from '@/api/finance';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { NO_DATA, formatRate, perYear } from '@/lib/rates';

import { RETURNS, retryUnlessRefused } from './queries';

/**
 * Spec 008 "Per asset": each asset's TWR and XIRR for the selected period, and a USD
 * asset's split into the asset's own move and the exchange rate. There is no list route,
 * so it is one `/api/returns/assets/{id}` call per asset (DEFERRED, 008 · CP4 and CP5),
 * under the same keys the asset's own page uses.
 */
export default function AssetReturnsTable({ positions, query }: { positions: Position[]; query: ReturnsQuery }) {
  const results = useQueries({
    queries: positions.map((position) => ({
      queryKey: [...RETURNS, 'asset', position.assetId, query],
      queryFn: () => api.assetReturns(position.assetId, query),
      retry: retryUnlessRefused,
    })),
  });

  if (positions.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="asset-returns-heading" className="grid gap-3">
      <SectionHeading id="asset-returns-heading">Por ativo</SectionHeading>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Ativo</TableHead>
            <TableHead className="text-right">TWR no período</TableHead>
            <TableHead className="text-right">XIRR</TableHead>
            <TableHead className="text-right">Ativo na moeda</TableHead>
            <TableHead className="text-right">Câmbio</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {positions.map((position, i) => (
            <TableRow key={position.assetId} data-testid={`asset-returns-${position.assetId}`}>
              <TableCell>
                <Link
                  to="/investments/$assetId/returns"
                  params={{ assetId: position.assetId }}
                  className="font-medium underline-offset-4 hover:underline"
                >
                  {position.ticker}
                </Link>
              </TableCell>
              <Figures position={position} result={results[i]?.data} failed={results[i]?.isError ?? false} />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </section>
  );
}

function Figures({ position, result, failed }: { position: Position; result: AssetReturns | undefined; failed: boolean }) {
  if (!result) {
    return (
      <TableCell colSpan={4} className="text-muted-foreground text-right text-sm">
        {failed ? 'Não foi possível carregar.' : 'Carregando…'}
      </TableCell>
    );
  }

  // A BRL asset has no split by design; a USD one without it had nothing to split.
  const noSplit = position.currency === 'BRL' ? 'Ativo em reais' : NO_DATA;

  return (
    <>
      <TableCell className="text-right tabular-nums">{formatRate(result.twr?.total)}</TableCell>
      <TableCell className="text-right tabular-nums">{perYear(result.xirr)}</TableCell>
      {result.fx ? (
        <>
          <TableCell className="text-right tabular-nums">{formatRate(result.fx.native)}</TableCell>
          <TableCell className="text-right tabular-nums">{formatRate(result.fx.fx)}</TableCell>
        </>
      ) : (
        <TableCell colSpan={2} className="text-muted-foreground text-right text-sm">
          {noSplit}
        </TableCell>
      )}
    </>
  );
}
