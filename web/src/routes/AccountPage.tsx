import { Link, useParams, useSearch } from '@tanstack/react-router';

import AccountDetails from '@/components/accounts/AccountDetails';
import AccountTransactions from '@/components/accounts/AccountTransactions';
import { useAccounts, useBalances } from '@/components/accounts/queries';
import Card from '@/components/Card';
import AccountImport from '@/components/import/AccountImport';
import type { AccountTab } from '@/lib/accounts';
import { accountTypeLabels } from '@/lib/labels';
import { cn } from '@/lib/utils';

const TABS: [AccountTab, string][] = [
  ['transactions', 'Lançamentos'],
  ['import', 'Importar extrato'],
  ['details', 'Detalhes da conta'],
];

/**
 * `/accounts/$accountId` (015): one account's name and balance over its tabs. The tab
 * is the `tab` search parameter, so a reload or a shared link opens the same one.
 */
export default function AccountPage(): React.JSX.Element | null {
  const { accountId } = useParams({ from: '/protected/accounts/$accountId' });
  const { tab = 'transactions' } = useSearch({ from: '/protected/accounts/$accountId' });
  const accounts = useAccounts();
  const summary = useBalances();

  if (!accounts.data) {
    return null;
  }

  const account = accounts.data.find((candidate) => candidate.id === accountId);

  if (!account) {
    return (
      <Card as="div" className="text-muted-foreground text-sm">
        <p>Conta não encontrada.</p>
      </Card>
    );
  }

  const balance = summary.data?.balances.find((entry) => entry.accountId === account.id)?.balance;

  return (
    <Card aria-labelledby="account-heading" className="grid gap-5">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="text-muted-foreground text-sm">{accountTypeLabels[account.type]}</p>
          <h3 id="account-heading" className="text-3xl font-semibold tracking-tight break-words">
            {account.name}
          </h3>
        </div>

        {balance !== undefined && (
          <div className="sm:text-right">
            <p className="text-muted-foreground text-sm">Saldo</p>
            <Balance value={balance} currency={account.currency} />
          </div>
        )}
      </header>

      <nav aria-label="Seções da conta" className="flex gap-1 overflow-x-auto border-b [scrollbar-width:none]">
        {TABS.map(([key, label]) => (
          <Link
            key={key}
            to="/accounts/$accountId"
            params={{ accountId }}
            search={key === 'transactions' ? {} : { tab: key }}
            // Exact, so the default tab's empty search is not a subset of every other tab's.
            activeOptions={{ exact: true }}
            className="text-muted-foreground hover:text-foreground data-[status=active]:border-primary data-[status=active]:text-primary -mb-px border-b-2 border-transparent px-3 py-2.5 text-sm font-bold whitespace-nowrap transition-colors"
          >
            {label}
          </Link>
        ))}
      </nav>

      {tab === 'transactions' && <AccountTransactions accountId={account.id} />}
      {/* Keyed, so another account starts its own wizard instead of inheriting a step. */}
      {tab === 'import' && <AccountImport key={account.id} account={account} />}
      {tab === 'details' && <AccountDetails key={account.id} account={account} />}
    </Card>
  );
}

/**
 * The balance as the dashboard's hero writes it, smaller: the symbol and the cents
 * quieter than the whole, and the sign printed as `Amount` prints it.
 */
function Balance({ value, currency }: { value: number; currency: string }) {
  const [whole, cents] = Math.abs(value)
    .toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    .split(',');

  return (
    <p data-testid="account-balance" className="font-display flex items-baseline gap-1 tracking-tight tabular-nums sm:justify-end">
      <span className="text-muted-foreground text-base">{currency === 'BRL' ? 'R$' : currency}</span>{' '}
      <span className={cn('text-4xl leading-none font-semibold', value < 0 && 'text-negative')}>
        {value < 0 ? '−' : '+'}
        {whole}
      </span>
      <span className="text-muted-foreground text-xl font-semibold">,{cents}</span>
    </p>
  );
}
