import { Link } from '@tanstack/react-router';

import type { DashboardSummary, NetWorthPoint } from '@/api/finance';
import Amount from '@/components/Amount';
import Card from '@/components/Card';
import { accountTypeLabels } from '@/lib/labels';
import { addMoney, netWorthChanges } from '@/lib/netWorth';
import { cn } from '@/lib/utils';

import NetWorthChart from './NetWorthChart';
import SectionHeading from './SectionHeading';

/**
 * Section 1, the hero (014, design N4): net worth in large figures — the accounts total
 * plus what is invested — how it moved over a month, the year and twelve months, and its
 * month-end sparkline; then one row per account.
 *
 * Every figure is today's and matches another screen: Em contas is the summary's total
 * (the BRL rows below add up to it), investido the series' last point (what
 * `/investments` shows). The chips compare their sum with earlier month-ends of the series.
 *
 * A credit card in debt is drawn in the destructive colour on top of `Amount`'s own
 * minus sign: money owed on a card reads differently from a checking account that
 * merely went down, and the sign still carries the meaning without the colour.
 */
export default function Balances({
  summary,
  netWorth,
  through,
}: {
  summary: DashboardSummary;
  netWorth: NetWorthPoint[];
  /** The local month, `YYYY-MM`, the chips count back from. */
  through: string;
}) {
  const invested = netWorth.at(-1)?.investments ?? 0;
  const total = addMoney(summary.total, invested);
  const changes = netWorthChanges(netWorth, total, through);

  return (
    <Card aria-labelledby="balances-heading" className="grid gap-6">
      {/* Side by side only from `xl`: beside the sidebar, a narrower card would squeeze the
          figure into wrapping. Below that the sparkline runs under it, full width. */}
      <div className="grid items-center gap-6 xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)] xl:gap-10 [&>*]:min-w-0">
        <div className="flex flex-col gap-3">
          <SectionHeading id="balances-heading">Patrimônio · contas + investimentos</SectionHeading>

          <p
            data-testid="net-worth-total"
            className="font-display flex flex-wrap items-baseline gap-x-2 tracking-tight tabular-nums"
          >
            <Hero value={total} />
          </p>

          {changes.length > 0 && (
            <ul aria-label="Variação do patrimônio" className="flex flex-wrap gap-2">
              {changes.map((change) => (
                <li
                  key={change.key}
                  data-testid={`net-worth-change-${change.key}`}
                  className={cn(
                    'rounded-full px-3 py-1.5 text-[0.8125rem] font-bold tabular-nums',
                    change.value >= 0 ? 'bg-positive/10 text-positive' : 'bg-negative/10 text-negative',
                  )}
                >
                  {change.label} {change.value < 0 ? '−' : '+'}
                  {format(change.value)}
                </li>
              ))}
            </ul>
          )}

          {/* Each label stays with its figure; on a phone the line breaks at the dot. */}
          <p className="text-muted-foreground text-sm">
            <span className="whitespace-nowrap">
              Em contas{' '}
              <span data-testid="total-balance">
                R$ <Amount value={summary.total} className={figure(summary.total)} />
              </span>
            </span>
            {invested !== 0 && (
              <>
                {' · '}
                <Link to="/investments" data-testid="hero-invested" className="whitespace-nowrap hover:underline">
                  investido R$ <Amount value={invested} className={figure(invested)} />
                </Link>
              </>
            )}
          </p>
        </div>

        <NetWorthChart series={netWorth} />
      </div>

      <ul className="grid content-start gap-x-6 border-t pt-1 sm:grid-cols-2 lg:grid-cols-3">
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

/** A holding, not an arrival: ink rather than the positive colour, the negative one below zero. */
const figure = (value: number) => cn('text-foreground font-semibold', value < 0 && 'text-negative');

/**
 * "R$" and the cents quieter than the reais, the way the eye reads a balance. The sign
 * is printed as `Amount` prints it, and the figure carries `Amount`'s test id, so the
 * total reads the same to the tests as every other amount. A step smaller on a phone,
 * so a seven-figure total still fits 390 px.
 */
function Hero({ value }: { value: number }) {
  const [reais, cents] = format(value).split(',');

  return (
    <>
      <span className="text-muted-foreground text-xl sm:text-2xl">R$</span>
      <span data-testid="amount" className="flex items-baseline">
        <span className={cn('text-4xl leading-none font-semibold sm:text-5xl 2xl:text-6xl', value < 0 && 'text-negative')}>
          {value < 0 ? '−' : '+'}
          {reais}
        </span>
        <span className="text-muted-foreground text-2xl font-semibold sm:text-3xl">,{cents}</span>
      </span>
    </>
  );
}
