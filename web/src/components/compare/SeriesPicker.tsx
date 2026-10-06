import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { useEffect, useState } from 'react';

import { api } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { compareBenchmarks, currencySymbol, marketAssetClassLabels } from '@/lib/labels';

import { MAX_SERIES } from './search';
import type { SeriesLabel } from './series';
import SeriesSwatch from './SeriesSwatch';

/** How many catalogue results are listed; a longer list asks for a narrower search. */
const SHOWN_ASSETS = 8;

/** Case and accents do not matter to a search: "dolar" finds "Dólar". */
const fold = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

/** The text once typing pauses, so the catalogue is not asked on every key. */
function useSettled(text: string, delayMs: number) {
  const [settled, setSettled] = useState(text);

  useEffect(() => {
    const timer = setTimeout(() => setSettled(text), delayMs);

    return () => clearTimeout(timer);
  }, [text, delayMs]);

  return settled;
}

/**
 * The chosen series as chips, and one search by ticker or name (decision 15): benchmarks are
 * matched here, catalogue assets by 006's search, as typing pauses.
 */
export default function SeriesPicker({
  keys,
  labels,
  onAdd,
  onRemove,
}: {
  keys: string[];
  labels: SeriesLabel[];
  onAdd: (key: string, label: SeriesLabel) => void;
  onRemove: (key: string) => void;
}) {
  const [text, setText] = useState('');
  const typed = text.trim();
  const q = useSettled(typed, 250);
  const full = keys.length >= MAX_SERIES;

  const assets = useQuery({
    queryKey: ['market-assets', q],
    queryFn: () => api.searchMarketAssets(q),
    enabled: q !== '' && !full,
  });
  const benchmarks =
    typed === '' ? [] : compareBenchmarks.filter((benchmark) => fold(`${benchmark.code} ${benchmark.name} ${benchmark.description}`).includes(fold(typed)));
  const found = [
    ...benchmarks.map((benchmark) => ({
      key: `benchmark:${benchmark.code}`,
      label: { label: benchmark.name, detail: benchmark.description },
      line: benchmark.description,
    })),
    ...(q === typed ? (assets.data ?? []) : []).slice(0, SHOWN_ASSETS).map((asset) => ({
      key: `asset:${asset.id}`,
      label: { label: asset.ticker, detail: asset.name !== asset.ticker ? asset.name : null },
      line: [asset.name, marketAssetClassLabels[asset.class] ?? asset.class, currencySymbol(asset.currency)].join(' · '),
    })),
  ];
  const searched = typed !== '' && q === typed && !assets.isFetching;

  return (
    <section aria-labelledby="compare-series-heading" className="glass grid gap-4 rounded-2xl p-5 sm:p-6">
      <SectionHeading id="compare-series-heading">Séries</SectionHeading>

      {keys.length > 0 && (
        <ul aria-label="Séries escolhidas" className="flex flex-wrap gap-2">
          {keys.map((key, index) => (
            <li key={key} className="bg-secondary flex max-w-full min-w-0 items-center gap-2 rounded-xl py-1 pr-1 pl-3 text-sm">
              <SeriesSwatch index={index} />
              <span className="truncate font-semibold">{labels[index]?.label}</span>
              <button
                type="button"
                aria-label={`Remover ${labels[index]?.label ?? ''}`}
                onClick={() => onRemove(key)}
                className="text-muted-foreground hover:bg-card hover:text-foreground focus-visible:ring-ring/50 flex size-7 shrink-0 items-center justify-center rounded-lg outline-none focus-visible:ring-[3px]"
              >
                <X className="size-4" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="grid gap-2">
        <Label htmlFor="compare-search">Buscar ativo ou índice</Label>
        <Input
          id="compare-search"
          type="search"
          autoComplete="off"
          placeholder="Ticker ou nome: ITUB4, BTC, CDI, dólar…"
          value={text}
          disabled={full}
          onChange={(event) => setText(event.target.value)}
        />
        {full && <p className="text-muted-foreground text-xs">Até {MAX_SERIES} séries por comparação. Remova uma para escolher outra.</p>}
      </div>

      {assets.isError && <Alert>Não foi possível buscar no catálogo.</Alert>}

      {!full && found.length > 0 && (
        <ul aria-label="Resultados da busca" className="bg-secondary divide-border divide-y rounded-xl">
          {found.map(({ key, label, line }) => {
            const chosen = keys.includes(key);

            return (
              <li key={key}>
                <button
                  type="button"
                  aria-label={chosen ? `${label.label}, já escolhida` : `Adicionar ${label.label}`}
                  disabled={chosen}
                  onClick={() => {
                    onAdd(key, label);
                    setText('');
                  }}
                  className="hover:bg-card focus-visible:ring-ring/50 flex w-full min-w-0 items-center justify-between gap-3 rounded-xl px-4 py-2 text-left outline-none focus-visible:ring-[3px] disabled:opacity-60"
                >
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold">{label.label}</span>
                    <span className="text-muted-foreground block truncate text-xs">{line}</span>
                  </span>
                  <span className="text-muted-foreground shrink-0 text-xs font-semibold">{chosen ? 'Escolhida' : 'Adicionar'}</span>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      {!full && searched && found.length === 0 && (
        <p className="text-muted-foreground text-sm">
          Nada encontrado com esse ticker ou nome. Ativos novos entram no catálogo em Dados de mercado ou em Investimentos.
        </p>
      )}
    </section>
  );
}
