import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { PortfolioSummary, Position } from '@/api/finance';
import Alert from '@/components/Alert';
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { formatDate } from '@/lib/labels';
import { STALE_AFTER_BUSINESS_DAYS, formatPercent, formatQuantity, formatUnitPrice, isStalePrice, localToday } from '@/lib/money';

import { usePortfolioSummary, usePositions } from './queries';
import { useDisplayCurrency, type Display } from './useDisplayCurrency';

/**
 * The positions table, in the API's order, by value.
 *
 * A position at zero — an asset just added, or one sold down to nothing — is hidden
 * until asked for: the table is what you hold. The total row is the API's summary,
 * never a sum made here. Money is in the page's currency; a unit price stays in the
 * asset's own.
 */
export default function Positions() {
  const positions = usePositions();
  const summary = usePortfolioSummary();
  const display = useDisplayCurrency();
  const [showClosed, setShowClosed] = useState(false);

  if (positions.isError || summary.isError) {
    return <Alert>Não foi possível carregar a carteira. Recarregue a página para tentar de novo.</Alert>;
  }

  if (!positions.data || !summary.data) {
    return <p className="text-muted-foreground text-sm">Carregando…</p>;
  }

  if (positions.data.length === 0) {
    return (
      <p className="text-muted-foreground glass rounded-2xl py-12 text-center text-sm">
        <span className="text-foreground block font-medium">Nenhum ativo na carteira ainda.</span>
        Adicione um ativo abaixo para registrar compras, vendas e proventos.
      </p>
    );
  }

  const closed = positions.data.filter((position) => position.quantity === 0).length;
  const shown = showClosed ? positions.data : positions.data.filter((position) => position.quantity !== 0);
  const today = localToday();

  return (
    <div className="grid gap-3">
      {closed > 0 && (
        <label className="text-muted-foreground flex items-center gap-2 text-sm">
          <input type="checkbox" checked={showClosed} onChange={(event) => setShowClosed(event.target.checked)} />
          Mostrar ativos sem posição ({closed})
        </label>
      )}

      {shown.length === 0 ? (
        <p className="text-muted-foreground glass rounded-2xl py-12 text-center text-sm">Nenhuma posição em aberto.</p>
      ) : (
        <PositionsTable positions={shown} summary={summary.data} today={today} display={display} />
      )}
    </div>
  );
}

function PositionsTable({
  positions,
  summary,
  today,
  display,
}: {
  positions: Position[];
  summary: PortfolioSummary;
  today: string;
  display: Display;
}) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Ativo</TableHead>
          <TableHead className="text-right">Quantidade</TableHead>
          <TableHead className="hidden text-right md:table-cell">Preço médio</TableHead>
          <TableHead className="hidden text-right sm:table-cell">Cotação</TableHead>
          <TableHead className="text-right">Valor</TableHead>
          <TableHead className="text-right">Resultado</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {positions.map((position) => (
          <TableRow key={position.assetId} data-testid={`position-${position.assetId}`}>
            <TableCell>
              <Link to="/investments/$assetId" params={{ assetId: position.assetId }} className="font-medium underline-offset-4 hover:underline">
                {position.ticker}
              </Link>
              <span className="text-muted-foreground block text-xs whitespace-normal">{position.nickname ?? position.name}</span>
            </TableCell>
            <TableCell className="text-right tabular-nums">{formatQuantity(position.quantity)}</TableCell>
            <TableCell className="hidden text-right tabular-nums md:table-cell">
              {formatUnitPrice(position.averageCost, position.currency)}
            </TableCell>
            <TableCell className="hidden text-right sm:table-cell">
              {position.price === null || position.priceDate === null ? (
                <span className="text-muted-foreground">Sem cotação</span>
              ) : (
                <>
                  <span className="tabular-nums">{formatUnitPrice(position.price, position.currency)}</span>
                  <span className="text-muted-foreground block text-xs tabular-nums">{formatDate(position.priceDate)}</span>
                  {isStalePrice(position.priceDate, today) && (
                    <span
                      data-testid="stale-price"
                      title={`Cotação de mais de ${STALE_AFTER_BUSINESS_DAYS} dias úteis atrás: a sincronização pode estar com problema.`}
                      className="text-destructive block text-xs font-medium"
                    >
                      Cotação desatualizada
                    </span>
                  )}
                </>
              )}
            </TableCell>
            <TableCell className="text-right tabular-nums">{position.valueBrl === null ? '—' : display.money(position.valueBrl)}</TableCell>
            <TableCell className="text-right tabular-nums">
              {position.unrealisedBrl === null ? '—' : display.signedMoney(position.unrealisedBrl)}
              {position.unrealisedPct !== null && (
                <span className="text-muted-foreground block text-xs">{formatPercent(position.unrealisedPct)}</span>
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
      <TableFooter>
        <TableRow data-testid="positions-total">
          <TableCell className="font-medium">Total</TableCell>
          <TableCell />
          <TableCell className="hidden md:table-cell" />
          <TableCell className="hidden sm:table-cell" />
          <TableCell className="text-right font-medium tabular-nums">{display.money(summary.totalBrl)}</TableCell>
          <TableCell className="text-right font-medium tabular-nums">{display.signedMoney(summary.unrealisedBrl)}</TableCell>
        </TableRow>
      </TableFooter>
    </Table>
  );
}
