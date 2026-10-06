import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { BreakdownKind } from '@/api/finance';
import AnalysisCard from '@/components/ai/AnalysisCard';
import Alert from '@/components/Alert';
import Card from '@/components/Card';
import Balances from '@/components/dashboard/Balances';
import CategoryBreakdown from '@/components/dashboard/CategoryBreakdown';
import MonthlyChart from '@/components/dashboard/MonthlyChart';
import MonthTotals from '@/components/dashboard/MonthTotals';
import RecentTransactions from '@/components/dashboard/RecentTransactions';
import PageHeader from '@/components/PageHeader';
import { useMonthly, useNetWorth, useSummary } from '@/components/dashboard/queries';
import { currentMonth } from '@/lib/months';

/**
 * `/`: where the money is, where it went this month, and how the last year
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
  const netWorth = useNetWorth(today);

  if (summary.isError || monthly.isError || netWorth.isError) {
    return (
      <Page>
        <Alert>Não foi possível carregar o painel. Recarregue a página para tentar de novo.</Alert>
      </Page>
    );
  }

  if (!summary.data || !monthly.data || !netWorth.data) {
    return (
      <Page>
        <p className="text-muted-foreground text-sm">Carregando…</p>
      </Page>
    );
  }

  // No accounts, a year of zeros and no net worth: nothing has been recorded, so a page
  // of zeros would only look broken. An account with no transactions is not this case —
  // its opening balance is already something to show — and neither is a portfolio held
  // with no account.
  const empty =
    summary.data.balances.length === 0 &&
    monthly.data.every((entry) => entry.income === 0 && entry.expense === 0) &&
    netWorth.data.length === 0;

  if (empty) {
    return (
      <Page>
        <Card as="div" data-testid="dashboard-empty" className="text-muted-foreground py-16 text-center">
          <p className="text-foreground text-sm font-medium">Nada para mostrar ainda</p>
          <p className="mt-1 text-sm">
            O painel se preenche assim que houver contas e lançamentos. Comece por um destes:
          </p>
          <p className="mt-4 flex justify-center gap-4 text-sm">
            <Link to="/transactions" className="text-primary font-semibold hover:underline">
              Registrar um lançamento
            </Link>
            <Link to="/accounts" className="text-primary font-semibold hover:underline">
              Importar um extrato
            </Link>
          </p>
        </Card>
      </Page>
    );
  }

  return (
    <Page>
      {/* The Obsidiana grid — the hero across, three cards under it, then the
          chart beside the latest transactions. */}
      <div className="grid gap-6 lg:grid-cols-6 [&>*]:min-w-0">
        <div className="lg:col-span-6">
          <Balances summary={summary.data} netWorth={netWorth.data} through={today} />
        </div>
        <div className="lg:col-span-2">
          <MonthTotals month={month} latest={today} totals={summary.data.month} onMonth={setMonth} />
        </div>
        <div className="lg:col-span-2">
          <CategoryBreakdown month={month} kind={kind} onKind={setKind} />
        </div>
        <div className="lg:col-span-2">
          {/* Keyed by month, so a refusal shown for one month is not carried to the next. */}
          <AnalysisCard key={month} month={month} />
        </div>
        <div className="lg:col-span-4">
          <MonthlyChart series={monthly.data} />
        </div>
        <div className="lg:col-span-2">
          <RecentTransactions />
        </div>
      </div>
    </Page>
  );
}

function Page({ children }: { children: React.ReactNode }) {
  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader title="Início" />
      {children}
    </section>
  );
}
