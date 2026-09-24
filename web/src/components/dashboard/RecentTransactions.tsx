import { Link } from '@tanstack/react-router';

import Amount from '@/components/Amount';
import { formatDate } from '@/lib/labels';

import { useRecent } from './queries';
import SectionHeading from './SectionHeading';

/** Section 5: the last ten transactions, newest first as the API orders them. */
export default function RecentTransactions() {
  const recent = useRecent(10);

  return (
    <section aria-labelledby="recent-heading" data-testid="recent-transactions">
      <div className="flex items-center justify-between gap-2">
        <SectionHeading id="recent-heading">Recentes</SectionHeading>
        <Link to="/transactions" className="text-sm underline">
          Ver todos os lançamentos
        </Link>
      </div>

      {recent.data?.items.length === 0 ? (
        <p className="text-muted-foreground mt-3 text-sm">Nenhum lançamento ainda.</p>
      ) : (
        <ul className="border-border mt-3 border-t">
          {(recent.data?.items ?? []).map((transaction) => (
            <li key={transaction.id} className="border-border flex items-center justify-between gap-4 border-b py-2">
              <span className="min-w-0">
                <span className="block truncate font-medium">{transaction.description}</span>
                <span className="text-muted-foreground block text-xs">
                  <span className="tabular-nums">{formatDate(transaction.date)}</span> · {transaction.categoryName} ·{' '}
                  {transaction.accountName}
                </span>
              </span>
              <Amount value={transaction.amount} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
