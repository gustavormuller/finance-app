import { Link } from '@tanstack/react-router';

import AddAsset from '@/components/investments/AddAsset';
import Positions from '@/components/investments/Positions';

/** `/investments` (007): what the user holds, valued by the latest daily row. */
export default function InvestmentsPage() {
  return (
    <section className="mx-auto grid max-w-5xl gap-10 px-4 py-8">
      <div className="flex flex-wrap items-baseline justify-between gap-4">
        <h2 className="text-2xl font-semibold tracking-tight">Investimentos</h2>
        <Link to="/investments/returns" className="text-sm font-medium underline-offset-4 hover:underline">
          Rentabilidade
        </Link>
      </div>

      <Positions />
      <AddAsset />
    </section>
  );
}
