import { Link } from '@tanstack/react-router';

import AddAsset from '@/components/investments/AddAsset';
import CurrencyNote from '@/components/investments/CurrencyNote';
import CurrencyToggle from '@/components/investments/CurrencyToggle';
import Positions from '@/components/investments/Positions';
import { useDisplayCurrency } from '@/components/investments/useDisplayCurrency';
import PageHeader from '@/components/PageHeader';
import { buttonVariants } from '@/components/ui/button';

/** `/investments` (007): what the user holds, valued by the latest daily row. */
export default function InvestmentsPage() {
  const display = useDisplayCurrency();

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader
        title="Investimentos"
        subtitle={display.fx || display.missingRate ? <CurrencyNote display={display} /> : undefined}
        actions={
          <>
            <CurrencyToggle />
            <Link to="/investments/returns" className={buttonVariants({ variant: 'outline' })}>
              Rentabilidade
            </Link>
          </>
        }
      />

      <Positions />
      <AddAsset />
    </section>
  );
}
