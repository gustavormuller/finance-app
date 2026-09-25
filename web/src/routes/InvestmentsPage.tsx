import { Link } from '@tanstack/react-router';

import AddAsset from '@/components/investments/AddAsset';
import Positions from '@/components/investments/Positions';
import PageHeader from '@/components/PageHeader';
import { buttonVariants } from '@/components/ui/button';

/** `/investments` (007): what the user holds, valued by the latest daily row. */
export default function InvestmentsPage() {
  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader
        title="Investimentos"
        actions={
          <Link to="/investments/returns" className={buttonVariants({ variant: 'outline' })}>
            Rentabilidade
          </Link>
        }
      />

      <Positions />
      <AddAsset />
    </section>
  );
}
