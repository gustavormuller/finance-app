import { Link } from '@tanstack/react-router';
import { Banknote, CreditCard, Landmark, PiggyBank, TrendingUp, type LucideIcon } from 'lucide-react';

import type { Account, AccountType, DashboardSummary, ImportBatch } from '@/api/finance';
import Amount from '@/components/Amount';
import { lastImportLine, type AccountTab } from '@/lib/accounts';
import { accountTypeLabels } from '@/lib/labels';
import { formatMoney } from '@/lib/money';
import { cn } from '@/lib/utils';

const ICONS: Record<AccountType, LucideIcon> = {
  Checking: Landmark,
  Savings: PiggyBank,
  CreditCard,
  Cash: Banknote,
  Investment: TrendingUp,
};

/**
 * The accounts as cards (015): icon by type, name, type and last import, and the
 * current balance, then the total. The name is the card's link and stretches over the
 * whole card, so the card is one click target and the link's name is just the name.
 *
 * A credit card in debt uses the destructive colour on top of `Amount`'s own minus
 * sign, as on the dashboard.
 */
export default function AccountList({
  accounts,
  summary,
  batches,
  selectedId,
  tab,
}: {
  accounts: Account[];
  summary: DashboardSummary | undefined;
  batches: ImportBatch[];
  selectedId: string | undefined;
  tab: AccountTab | undefined;
}): React.JSX.Element {
  const balances = new Map(summary?.balances.map((entry) => [entry.accountId, entry.balance]));

  return (
    <div className="grid content-start gap-3">
      <ul aria-label="Suas contas" className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 [&>*]:min-w-0">
        {accounts.map((account) => {
          const Icon = ICONS[account.type];
          const selected = account.id === selectedId;
          const balance = balances.get(account.id);

          return (
            <li
              key={account.id}
              data-testid={`account-card-${account.id}`}
              className={cn(
                'relative flex items-center gap-3 rounded-2xl border p-3.5 transition-colors',
                selected ? 'glass border-primary ring-primary/15 ring-[3px]' : 'hover:bg-secondary border-transparent',
              )}
            >
              <span
                className={cn(
                  'flex size-10 shrink-0 items-center justify-center rounded-xl',
                  selected ? 'bg-accent text-primary' : 'bg-secondary text-muted-foreground',
                )}
              >
                <Icon className="size-5" aria-hidden="true" />
              </span>

              {/* The balance shares the name's line, so the line under it has the card's
                  whole width: the last import is that line's news, and it wraps rather
                  than being cut. */}
              <span className="min-w-0 flex-1">
                <span className="flex items-baseline justify-between gap-3">
                  <Link
                    to="/accounts/$accountId"
                    params={{ accountId: account.id }}
                    // Switching accounts keeps the tab: importing several statements in a row.
                    search={tab ? { tab } : {}}
                    activeOptions={{ includeSearch: false }}
                    className="min-w-0 truncate text-[0.9375rem] font-bold outline-none after:absolute after:inset-0 after:rounded-2xl focus-visible:after:ring-[3px] focus-visible:after:ring-ring/50"
                  >
                    {account.name}
                  </Link>
                  {balance !== undefined && (
                    <Amount
                      value={balance}
                      className={cn(
                        'shrink-0 text-sm font-bold',
                        account.type === 'CreditCard' && balance < 0 && 'text-destructive',
                      )}
                    />
                  )}
                </span>
                <span className="text-muted-foreground block text-xs">
                  {accountTypeLabels[account.type]} · {lastImportLine(batches, account.id)}
                </span>
              </span>
            </li>
          );
        })}
      </ul>

      {summary && (
        <p data-testid="accounts-total" className="bg-secondary text-muted-foreground rounded-2xl p-3.5 text-sm">
          Total em contas
          <strong className="font-display text-foreground mt-1 block text-2xl font-semibold tabular-nums">
            {formatMoney(summary.total)}
          </strong>
        </p>
      )}
    </div>
  );
}
