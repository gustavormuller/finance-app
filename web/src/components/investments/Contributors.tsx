import { Link } from '@tanstack/react-router';

import type { Position } from '@/api/finance';
import Card from '@/components/Card';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { marketAssetClassLabels } from '@/lib/labels';
import { formatPercent } from '@/lib/money';
import { signTone } from '@/lib/rates';
import { cn } from '@/lib/utils';

import type { Display } from './useDisplayCurrency';

const LIMIT = 8;

type Valued = Position & { unrealisedBrl: number };

/**
 * The positions whose result weighs most (016, decision 5): the largest by size, gains
 * and losses alike, then listed from the largest gain to the largest loss. Ordering
 * only; every figure is the API's.
 */
function ranked(positions: Position[]): Valued[] {
  return positions
    .filter((position): position is Valued => position.quantity !== 0 && position.unrealisedBrl !== null)
    .sort((a, b) => Math.abs(b.unrealisedBrl) - Math.abs(a.unrealisedBrl))
    .slice(0, LIMIT)
    .sort((a, b) => b.unrealisedBrl - a.unrealisedBrl);
}

/** "O que mais contribuiu" (016, design P1): each position's unrealised result, as a bar. */
export default function Contributors({ positions, display }: { positions: Position[]; display: Display }) {
  const top = ranked(positions);
  const largest = Math.max(0, ...top.map((position) => Math.abs(position.unrealisedBrl)));
  const open = positions.filter((position) => position.quantity !== 0).length;

  return (
    <Card aria-labelledby="contributors-heading" data-testid="contributors" className="grid content-start gap-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <SectionHeading id="contributors-heading">O que mais contribuiu</SectionHeading>
        <Link to="/investments" hash="posicoes" className="text-primary text-sm font-medium underline-offset-4 hover:underline">
          {open === 1 ? 'Ver a posição' : `Todas as ${open} posições`}
        </Link>
      </div>

      <ul>
        {top.map((position) => {
          const result = position.unrealisedBrl;
          const width = largest === 0 ? 0 : (Math.abs(result) / largest) * 100;

          return (
            <li key={position.assetId} data-testid={`contributor-${position.assetId}`} className="border-border grid gap-1.5 border-t py-2.5">
              <div className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 truncate">
                  <Link
                    to="/investments/$assetId"
                    params={{ assetId: position.assetId }}
                    className="font-display font-semibold underline-offset-4 hover:underline"
                  >
                    {position.ticker}
                  </Link>
                  <span className="text-muted-foreground ml-2 text-xs">{marketAssetClassLabels[position.class]}</span>
                </span>
                <span className={cn('font-bold whitespace-nowrap tabular-nums', signTone(result))}>{display.signedMoney(result)}</span>
              </div>
              <div className="flex items-center gap-3">
                <span className="bg-secondary h-1.5 min-w-0 flex-1 rounded-full" aria-hidden="true">
                  <span
                    data-testid="result-bar"
                    className={cn('block h-1.5 rounded-full', result < 0 ? 'bg-negative' : 'bg-positive')}
                    style={{ width: `${width}%` }}
                  />
                </span>
                <span className="text-muted-foreground text-xs whitespace-nowrap tabular-nums">
                  {position.unrealisedPct === null ? '' : `${formatPercent(position.unrealisedPct)} · `}
                  {position.valueBrl === null ? '' : display.compactMoney(position.valueBrl)}
                </span>
              </div>
            </li>
          );
        })}
      </ul>
    </Card>
  );
}
