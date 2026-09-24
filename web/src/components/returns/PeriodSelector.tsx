import { useState } from 'react';

import type { ReturnsPeriodKind, ReturnsQuery } from '@/api/finance';
import FormField from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { returnsPeriodLabels, returnsPeriods } from '@/lib/labels';

/**
 * Spec 008 "Period selector": inception, YTD, 12m, or a custom range. A preset applies
 * on click; a custom range applies on "Aplicar", so typing a date does not fetch on
 * every keystroke. The API's 400s for `from`/`to` show under their dates, as sent.
 */
export default function PeriodSelector({
  query,
  errors,
  onChange,
}: {
  query: ReturnsQuery;
  errors: Record<string, string[]>;
  onChange: (query: ReturnsQuery) => void;
}) {
  const [selected, setSelected] = useState<ReturnsPeriodKind>(query.period);
  const [from, setFrom] = useState(query.from ?? '');
  const [to, setTo] = useState(query.to ?? '');

  const choose = (period: ReturnsPeriodKind) => {
    setSelected(period);
    if (period !== 'custom') {
      onChange({ period });
    }
  };

  return (
    <div className="grid gap-3">
      <div role="group" aria-label="Período" className="flex flex-wrap gap-1">
        {returnsPeriods.map((period) => (
          <Button
            key={period}
            type="button"
            size="sm"
            variant={selected === period ? 'secondary' : 'ghost'}
            aria-pressed={selected === period}
            onClick={() => choose(period)}
          >
            {returnsPeriodLabels[period]}
          </Button>
        ))}
      </div>

      {selected === 'custom' && (
        <form
          aria-label="Período personalizado"
          className="grid items-end gap-3 sm:grid-cols-[repeat(2,minmax(0,12rem))_auto]"
          onSubmit={(event) => {
            event.preventDefault();
            onChange({ period: 'custom', from, to });
          }}
        >
          <FormField id="returns-from" label="De" errors={errors.from}>
            <Input id="returns-from" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
          </FormField>
          <FormField id="returns-to" label="Até" errors={errors.to}>
            <Input id="returns-to" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          </FormField>
          <Button type="submit" variant="outline">
            Aplicar
          </Button>
        </form>
      )}
    </div>
  );
}
