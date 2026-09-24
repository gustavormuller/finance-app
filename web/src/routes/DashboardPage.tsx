import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { BreakdownKind } from '@/api/finance';
import Alert from '@/components/Alert';
import Balances from '@/components/dashboard/Balances';
import CategoryBreakdown from '@/components/dashboard/CategoryBreakdown';
import MonthlyChart from '@/components/dashboard/MonthlyChart';
import MonthTotals from '@/components/dashboard/MonthTotals';
import RecentTransactions from '@/components/dashboard/RecentTransactions';
import { useMonthly, useSummary } from '@/components/dashboard/queries';
import { currentMonth } from '@/lib/months';

/**
 * `/` (005): where the money is, where it went this month, and how the last year
 * looks. Every number comes from the API already aggregated; the page only lays it
 * out.
 */
export default function DashboardPage() {
  // Read once per visit: a page left open across midnight on the 1st keeps its month.
  const [today] = useState(currentMonth);
  const [month, setMonth] = useState(today);
  const [kind, setKind] = useState<BreakdownKind>('Expense');

  const summary = useSummary(month);
  const monthly = useMonthly(today);

  if (summary.isError || monthly.isError) {
    return (
      <Page>
        <Alert>Não foi possível carregar o painel. Recarregue a página para tentar de novo.</Alert>
      </Page>
    );
  }

  if (!summary.data || !monthly.data) {
    return (
      <Page>
        <p className="text-muted-foreground text-sm">Carregando…</p>
      </Page>
    );
  }

  // No accounts and a year of zeros: nothing has been recorded, so a page of zeros
  // would only look broken. An account with no transactions is not this case — its
  // opening balance is already something to show.
  const empty =
    summary.data.balances.length === 0 &&
    monthly.data.every((entry) => entry.income === 0 && entry.expense === 0);

  if (empty) {
    return (
      <Page>
        <div data-testid="dashboard-empty" className="border-border text-muted-foreground border-t py-16 text-center">
          <p className="text-foreground text-sm font-medium">Nada para mostrar ainda</p>
          <p className="mt-1 text-sm">
            O painel se preenche assim que houver contas e lançamentos. Comece por um destes:
          </p>
          <p className="mt-4 flex justify-center gap-4 text-sm">
            <Link to="/transactions" className="text-foreground underline">
              Registrar um lançamento
            </Link>
            <Link to="/import" className="text-foreground underline">
              Importar um extrato
            </Link>
          </p>
        </div>
      </Page>
    );
  }

  return (
    <Page>
      <div className="grid gap-10">
        <Balances summary={summary.data} />
        <MonthTotals month={month} latest={today} totals={summary.data.month} onMonth={setMonth} />
        <MonthlyChart series={monthly.data} />
        <CategoryBreakdown month={month} kind={kind} onKind={setKind} />
        <RecentTransactions />
      </div>
    </Page>
  );
}

function Page({ children }: { children: React.ReactNode }) {
  return (
    <section className="mx-auto max-w-5xl px-4 py-8">
      <h2 className="mb-6 text-2xl font-semibold tracking-tight">Início</h2>
      {children}
    </section>
  );
}
