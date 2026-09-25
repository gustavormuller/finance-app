import AssetCatalogue from '@/components/market-data/AssetCatalogue';
import SyncRuns from '@/components/market-data/SyncRuns';

/**
 * 006's plumbing screen: whether the market-data sync works. Not in the navigation; the
 * spec reaches it from settings later, and until then by its address, `/market-data`.
 */
export default function MarketDataPage() {
  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <div>
        <h2 className="text-3xl font-semibold tracking-tight">Dados de mercado</h2>
        <p className="text-muted-foreground mt-1 text-sm">
          Cotações e índices compartilhados por todos os usuários, atualizados todas as noites.
        </p>
      </div>

      <SyncRuns />
      <AssetCatalogue />
    </section>
  );
}
