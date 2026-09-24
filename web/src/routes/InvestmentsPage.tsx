import Positions from '@/components/investments/Positions';

/** `/investments` (007): what the user holds, valued by the latest daily row. */
export default function InvestmentsPage() {
  return (
    <section className="mx-auto grid max-w-5xl gap-10 px-4 py-8">
      <h2 className="text-2xl font-semibold tracking-tight">Investimentos</h2>

      <Positions />
    </section>
  );
}
