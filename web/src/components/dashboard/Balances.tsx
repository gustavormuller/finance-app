import type { DashboardSummary } from '@/api/finance';
import Amount from '@/components/Amount';
import { accountTypeLabels } from '@/lib/labels';
import { cn } from '@/lib/utils';

import SectionHeading from './SectionHeading';

/**
 * Section 1: the total, then one row per account.
 *
 * A credit card in debt is drawn in the destructive colour on top of `Amount`'s own
 * minus sign: money owed on a card reads differently from a checking account that
 * merely went down, and the sign still carries the meaning without the colour.
 */
export default function Balances({ summary }: { summary: DashboardSummary }) {
  return (
    <section aria-labelledby="balances-heading">
      <SectionHeading id="balances-heading">Saldo total</SectionHeading>

      <p data-testid="total-balance" className="mt-1 text-3xl font-semibold">
        <Amount value={summary.total} />
      </p>

      <ul className="border-border mt-4 border-t">
        {summary.balances.map((account) => (
          <li
            key={account.accountId}
            data-testid={`account-balance-${account.accountId}`}
            className="border-border flex items-center justify-between gap-4 border-b py-2"
          >
            <span>
              <span className="font-medium">{account.name}</span>
              <span className="text-muted-foreground block text-xs">
                {accountTypeLabels[account.type]}
                {account.excludedFromTotal && ` · ${account.currency}, fora do total`}
              </span>
            </span>

            <Amount
              value={account.balance}
              className={cn(account.type === 'CreditCard' && account.balance < 0 && 'text-destructive')}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}
