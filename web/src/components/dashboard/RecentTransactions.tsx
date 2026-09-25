import { Link } from '@tanstack/react-router';

import Amount from '@/components/Amount';
import Card from '@/components/Card';
import { formatDate } from '@/lib/labels';

import { useRecent } from './queries';
import SectionHeading from './SectionHeading';

/** Section 5: the last ten transactions, newest first as the API orders them. */
export default function RecentTransactions() {
  const recent = useRecent(10);

  return (
    <Card aria-labelledby="recent-heading" data-testid="recent-transactions" className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-2">
        <SectionHeading id="recent-heading">Recentes</SectionHeading>
        <Link to="/transactions" className="text-primary text-sm font-semibold hover:underline">
          Ver todos os lançamentos
        </Link>
      </div>

      {recent.data?.items.length === 0 ? (
        <p className="text-muted-foreground text-sm">Nenhum lançamento ainda.</p>
      ) : (
        <ul>
          {(recent.data?.items ?? []).map((transaction) => (
            <li key={transaction.id} className="flex items-center justify-between gap-4 border-t py-2.5">
              <span className="min-w-0">
                <span className="block truncate text-sm font-semibold">{transaction.description}</span>
                <span className="text-muted-foreground block truncate text-xs">
                  <span className="tabular-nums">{formatDate(transaction.date)}</span> · {transaction.categoryName} ·{' '}
                  {transaction.accountName}
                </span>
              </span>
              <Amount value={transaction.amount} className="shrink-0 text-sm font-semibold" />
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
