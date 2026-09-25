import { Link } from '@tanstack/react-router';

import type { DashboardSummary } from '@/api/finance';
import Amount from '@/components/Amount';
import Card from '@/components/Card';
import { usePortfolioSummary } from '@/components/investments/queries';
import { accountTypeLabels } from '@/lib/labels';
import { cn } from '@/lib/utils';

import SectionHeading from './SectionHeading';

/**
 * Section 1, the hero (012): the total in large figures, what is invested beside it
 * when there are positions, then one row per account.
 *
 * A credit card in debt is drawn in the destructive colour on top of `Amount`'s own
 * minus sign: money owed on a card reads differently from a checking account that
 * merely went down, and the sign still carries the meaning without the colour.
 */
export default function Balances({ summary }: { summary: DashboardSummary }) {
  const portfolio = usePortfolioSummary();
  const invested = portfolio.data && portfolio.data.totalBrl > 0 ? portfolio.data : null;

  return (
    <Card aria-labelledby="balances-heading" className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] lg:gap-10">
      <div className="flex min-w-0 flex-col gap-3">
        <SectionHeading id="balances-heading">Saldo total</SectionHeading>

        <p data-testid="total-balance" className="font-display flex items-baseline gap-2 tracking-tight tabular-nums">
          <Hero value={summary.total} />
        </p>

        {invested && (
          <Link
            to="/investments"
            data-testid="hero-invested"
            className="bg-secondary hover:bg-accent flex flex-wrap items-center gap-x-3 gap-y-1 self-start rounded-xl px-3 py-2 text-sm transition-colors"
          >
            <span className="text-muted-foreground">Investido</span>
            <span className="font-semibold tabular-nums">R$ {format(invested.totalBrl)}</span>
            <Amount value={invested.unrealisedBrl} className="text-left font-semibold" />
            <span className="text-muted-foreground">sobre o custo</span>
          </Link>
        )}
      </div>

      <ul className="grid content-start gap-x-6 sm:grid-cols-2">
        {summary.balances.map((account) => (
          <li
            key={account.accountId}
            data-testid={`account-balance-${account.accountId}`}
            className="flex items-center justify-between gap-4 border-b py-2.5"
          >
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold">{account.name}</span>
              <span className="text-muted-foreground block text-xs">
                {accountTypeLabels[account.type]}
                {account.excludedFromTotal && ` · ${account.currency}, fora do total`}
              </span>
            </span>

            <Amount
              value={account.balance}
              className={cn('text-sm font-semibold', account.type === 'CreditCard' && account.balance < 0 && 'text-destructive')}
            />
          </li>
        ))}
      </ul>
    </Card>
  );
}

const format = (value: number) =>
  Math.abs(value).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * "R$" and the cents quieter than the reais, the way the eye reads a balance. The sign
 * is printed as `Amount` prints it, and the figure carries `Amount`'s test id, so the
 * total reads the same to the tests as every other amount.
 */
function Hero({ value }: { value: number }) {
  const [reais, cents] = format(value).split(',');

  return (
    <>
      <span className="text-muted-foreground text-2xl">R$</span>
      <span data-testid="amount" className="flex items-baseline">
        <span className={cn('text-5xl leading-none font-semibold sm:text-6xl', value < 0 && 'text-negative')}>
          {value < 0 ? '−' : '+'}
          {reais}
        </span>
        <span className="text-muted-foreground text-3xl font-semibold">,{cents}</span>
      </span>
    </>
  );
}
