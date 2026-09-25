import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { api } from '@/api/finance';
import Amount from '@/components/Amount';
import { formatDate } from '@/lib/labels';

const LATEST = 10;

/**
 * "Lançamentos" (015): the account's latest transactions, newest first as the API
 * orders them, and the way to all of them. Under `['transactions']`, so every write to
 * a transaction, and every import, refreshes it.
 */
export default function AccountTransactions({ accountId }: { accountId: string }): React.JSX.Element {
  const query = { accountId, page: 1, pageSize: LATEST };
  const transactions = useQuery({
    queryKey: ['transactions', query],
    queryFn: () => api.listTransactions(query),
  });

  return (
    <div className="grid gap-3">
      {transactions.isError && (
        <p className="text-muted-foreground text-sm">Não foi possível carregar os lançamentos.</p>
      )}

      {transactions.data?.items.length === 0 ? (
        <p className="text-muted-foreground py-6 text-center text-sm">Nenhum lançamento nesta conta ainda.</p>
      ) : (
        <ul>
          {(transactions.data?.items ?? []).map((transaction) => (
            <li
              key={transaction.id}
              data-testid={`account-transaction-${transaction.id}`}
              className="flex items-center justify-between gap-4 border-b py-2.5"
            >
              <span className="min-w-0">
                <span className="block truncate text-sm font-semibold">{transaction.description}</span>
                <span className="text-muted-foreground block truncate text-xs">
                  <span className="tabular-nums">{formatDate(transaction.date)}</span> · {transaction.categoryName}
                </span>
              </span>
              <Amount value={transaction.amount} className="shrink-0 text-sm font-semibold" />
            </li>
          ))}
        </ul>
      )}

      <Link
        to="/transactions"
        search={{ accountId }}
        className="text-primary justify-self-start text-sm font-semibold hover:underline"
      >
        Ver todos os lançamentos da conta
      </Link>
    </div>
  );
}
