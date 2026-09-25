import { Link } from '@tanstack/react-router';

import SectionHeading from '@/components/dashboard/SectionHeading';
import AddAsset from '@/components/investments/AddAsset';
import CurrencyNote from '@/components/investments/CurrencyNote';
import CurrencyToggle from '@/components/investments/CurrencyToggle';
import Positions from '@/components/investments/Positions';
import { usePositions } from '@/components/investments/queries';
import { useDisplayCurrency } from '@/components/investments/useDisplayCurrency';
import PageHeader from '@/components/PageHeader';
import ReturnsHero from '@/components/returns/ReturnsHero';
import { buttonVariants } from '@/components/ui/button';

/**
 * `/investments` (007, laid out by 016's design P1): how the portfolio is doing first,
 * then what is held. The returns lead; the positions table and "Adicionar ativo" follow
 * under "Posições", unchanged. With nothing held there are no returns to show.
 */
export default function InvestmentsPage() {
  const display = useDisplayCurrency();
  const held = (usePositions().data?.length ?? 0) > 0;

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

      {held && <ReturnsHero currency={display.chosen} />}

      <section id="posicoes" aria-labelledby="positions-heading" className="grid scroll-mt-6 gap-3 [&>*]:min-w-0">
        <SectionHeading id="positions-heading">Posições</SectionHeading>
        <Positions />
      </section>

      <AddAsset />
    </section>
  );
}
