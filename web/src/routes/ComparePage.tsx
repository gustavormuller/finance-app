import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';

import type { CompareQuery, Comparison } from '@/api/finance';
import CompareControls from '@/components/compare/CompareControls';
import CompareResult from '@/components/compare/CompareResult';
import { MIN_SERIES, periodOf, seriesKeys, withPatch, type CompareSearchPatch } from '@/components/compare/search';
import { COMPARE, benchmarkCode, benchmarkLabel, labelOf, useComparison, type SeriesLabel } from '@/components/compare/series';
import SeriesPicker from '@/components/compare/SeriesPicker';
import PageHeader from '@/components/PageHeader';

/**
 * `/compare` (026): two to six catalogue assets and benchmark series side by side, base 100,
 * each in its own currency or converted at PTAX. Everything on screen is the API's; the
 * address holds the selection and the settings, so a comparison can be bookmarked.
 */
export default function ComparePage() {
  const search = useSearch({ from: '/protected/compare' });
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [picked, setPicked] = useState<Record<string, SeriesLabel>>({});

  const keys = seriesKeys(search.series);
  const query: CompareQuery = {
    series: keys,
    period: periodOf(search),
    from: search.from,
    to: search.to,
    currency: search.currency ?? 'original',
  };
  const enough = keys.length >= MIN_SERIES;
  const result = useComparison(query, enough);

  const update = (patch: CompareSearchPatch) =>
    void navigate({ to: '/compare', search: (current) => withPatch(current, patch), replace: true });

  // A name picked here, else one any comparison answered, else a benchmark's own; an asset
  // reached only through the address, alone, reads "Ativo" until a second series is chosen.
  const labels = keys.map((key): SeriesLabel => {
    if (picked[key]) {
      return picked[key];
    }

    for (const [, data] of queryClient.getQueriesData<Comparison>({ queryKey: COMPARE })) {
      const found = data?.series.find((series) => series.key === key);
      if (found) {
        return labelOf(found);
      }
    }

    const code = benchmarkCode(key);
    return code ? benchmarkLabel(code) : { label: 'Ativo', detail: null };
  });

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader title="Comparar" subtitle="Ativos e índices lado a lado, base 100." />

      <SeriesPicker
        keys={keys}
        labels={labels}
        onAdd={(key, label) => {
          setPicked((current) => ({ ...current, [key]: label }));
          update({ series: [...keys, key].join(',') });
        }}
        onRemove={(key) => update({ series: keys.filter((kept) => kept !== key).join(',') || undefined })}
      />

      <CompareControls search={search} onChange={update} />

      {enough ? (
        <CompareResult result={result} labels={labels} log={search.scale === 'log'} />
      ) : (
        <div data-testid="compare-empty" className="text-muted-foreground glass rounded-2xl px-6 py-12 text-center text-sm">
          <p className="text-foreground font-medium">Escolha pelo menos duas séries para comparar.</p>
          <p className="mt-1">Busque um ativo pelo ticker ou pelo nome, ou um índice como CDI, IPCA ou dólar.</p>
        </div>
      )}
    </section>
  );
}
