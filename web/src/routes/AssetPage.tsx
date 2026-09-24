import { Link, useParams } from '@tanstack/react-router';

import { usePositions } from '@/components/investments/queries';

/** `/investments/{id}` (007): one asset, its movements and its value over time. */
export default function AssetPage() {
  const { assetId } = useParams({ from: '/protected/investments/$assetId' });
  const positions = usePositions();
  const position = positions.data?.find((candidate) => candidate.assetId === assetId);

  return (
    <section className="mx-auto grid max-w-5xl gap-10 px-4 py-8">
      <div>
        <Link to="/investments" className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← Investimentos
        </Link>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight">{position?.ticker ?? 'Ativo'}</h2>
      </div>
    </section>
  );
}
