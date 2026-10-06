import { Link } from '@tanstack/react-router';

import SectionHeading from '@/components/dashboard/SectionHeading';
import AddAsset from '@/components/investments/AddAsset';
import Contributors from '@/components/investments/Contributors';
import CurrencyNote from '@/components/investments/CurrencyNote';
import CurrencyToggle from '@/components/investments/CurrencyToggle';
import Holdings from '@/components/investments/Holdings';
import Positions from '@/components/investments/Positions';
import { usePortfolioSummary, usePositions } from '@/components/investments/queries';
import { useDisplayCurrency } from '@/components/investments/useDisplayCurrency';
import PageHeader from '@/components/PageHeader';
import ReturnsHero from '@/components/returns/ReturnsHero';
import { buttonVariants } from '@/components/ui/button';

/**
 * `/investments`, laid out by 016's design P1: how the portfolio is doing first, then
 * what is held. The returns lead; the positions table and "Adicionar ativo" follow
 * under "Posições". With nothing held there are no returns to show.
 */
export default function InvestmentsPage() {
  const display = useDisplayCurrency();
  const positions = usePositions().data ?? [];
  const summary = usePortfolioSummary().data;
  const valued = positions.some((position) => position.quantity !== 0 && position.valueBrl !== null);

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader
        title="Investimentos"
        subtitle={display.fx || display.missingRate ? <CurrencyNote display={display} /> : undefined}
        actions={
          <>
            <CurrencyToggle />
            <Link to="/investments" hash="posicoes" className={buttonVariants({ variant: 'outline' })}>
              Posições
            </Link>
            <Link to="/investments/returns" className={buttonVariants({ variant: 'outline' })}>
              Rentabilidade
            </Link>
          </>
        }
      />

      {positions.length > 0 && <ReturnsHero currency={display.chosen} />}

      {valued && summary && (
        <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.45fr)_minmax(0,1fr)] [&>*]:min-w-0">
          <Contributors positions={positions} display={display} />
          <Holdings summary={summary} display={display} />
        </div>
      )}

      <section id="posicoes" aria-labelledby="positions-heading" className="grid scroll-mt-6 gap-3 [&>*]:min-w-0">
        <SectionHeading id="positions-heading">Posições</SectionHeading>
        <Positions />
      </section>

      <AddAsset />
    </section>
  );
}
