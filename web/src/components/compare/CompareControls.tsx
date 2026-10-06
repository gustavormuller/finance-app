import { useState } from 'react';

import FormField from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { compareCurrencies, compareCurrencyLabels, comparePeriodLabels, comparePeriods, comparePeriodTitles } from '@/lib/labels';
import { cn } from '@/lib/utils';

import { periodOf, type CompareSearch, type CompareSearchPatch } from './search';

/** A choice among a few, pressed like the investments' R$ | US$ toggle. */
function Segmented<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: [T, string][];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div role="group" aria-label={label} className="bg-secondary flex max-w-full flex-wrap items-center gap-0.5 rounded-xl p-1">
      {options.map(([option, text]) => (
        <button
          key={option}
          type="button"
          aria-pressed={value === option}
          onClick={() => onChange(option)}
          className={cn(
            'text-muted-foreground hover:text-foreground focus-visible:ring-ring/50 h-8 rounded-lg px-3 text-sm font-semibold whitespace-nowrap transition-colors outline-none focus-visible:ring-[3px]',
            value === option && 'bg-card text-foreground shadow-sm',
          )}
        >
          {text}
        </button>
      ))}
    </div>
  );
}

/**
 * The period (decision 5), the currency (decision 10) and the axis (decision 13), each written
 * to the address by `onChange`. A preset applies on click; a custom range on "Aplicar", so a
 * date being typed is not a request.
 */
export default function CompareControls({
  search,
  onChange,
}: {
  search: CompareSearch;
  onChange: (patch: CompareSearchPatch) => void;
}) {
  const period = periodOf(search);
  const [editing, setEditing] = useState(false);
  const pressed = editing ? 'custom' : period;

  return (
    <div className="grid gap-3">
      <div role="group" aria-label="Período" className="flex flex-wrap gap-1">
        {comparePeriods.map((option) => (
          <Button
            key={option}
            type="button"
            size="sm"
            variant={pressed === option ? 'secondary' : 'ghost'}
            aria-pressed={pressed === option}
            title={comparePeriodTitles[option]}
            onClick={() => {
              setEditing(option === 'custom');
              if (option !== 'custom') {
                onChange({ period: option, from: undefined, to: undefined });
              }
            }}
          >
            {comparePeriodLabels[option]}
          </Button>
        ))}
      </div>

      {pressed === 'custom' && (
        <CustomPeriod
          key={`${search.from ?? ''}|${search.to ?? ''}`}
          from={search.from ?? ''}
          to={search.to ?? ''}
          onApply={(from, to) => {
            setEditing(false);
            onChange({ period: 'custom', from: from || undefined, to: to || undefined });
          }}
        />
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Segmented
          label="Moeda"
          options={compareCurrencies.map((currency) => [currency, compareCurrencyLabels[currency]])}
          value={search.currency ?? 'original'}
          onChange={(currency) => onChange({ currency: currency === 'original' ? undefined : currency })}
        />
        <Segmented
          label="Escala"
          options={[
            ['linear', 'Linear'],
            ['log', 'Log'],
          ]}
          value={search.scale ?? 'linear'}
          onChange={(scale) => onChange({ scale: scale === 'log' ? 'log' : undefined })}
        />
      </div>
    </div>
  );
}

function CustomPeriod({ from, to, onApply }: { from: string; to: string; onApply: (from: string, to: string) => void }) {
  const [start, setStart] = useState(from);
  const [end, setEnd] = useState(to);

  return (
    <form
      aria-label="Período personalizado"
      className="grid items-end gap-3 sm:grid-cols-[repeat(2,minmax(0,12rem))_auto]"
      onSubmit={(event) => {
        event.preventDefault();
        onApply(start, end);
      }}
    >
      <FormField id="compare-from" label="De">
        <Input id="compare-from" type="date" value={start} onChange={(event) => setStart(event.target.value)} />
      </FormField>
      <FormField id="compare-to" label="Até">
        <Input id="compare-to" type="date" value={end} onChange={(event) => setEnd(event.target.value)} />
      </FormField>
      <Button type="submit" variant="outline" className="justify-self-start">
        Aplicar
      </Button>
    </form>
  );
}
