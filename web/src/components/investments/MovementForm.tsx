import { useState } from 'react';

import type { Movement, MovementInput, MovementKind } from '@/api/finance';
import FormField, { selectClasses } from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { movementTotal, toApiNumber } from '@/lib/decimal';
import { movementKindLabels, movementKinds } from '@/lib/labels';
import { formatMoney } from '@/lib/money';

/** The fields rendered, so an API message naming another one goes to the alert instead. */
const FIELDS = ['kind', 'date', 'quantity', 'unitPrice', 'amount', 'fees', 'notes'] as const;

const TOTAL_LABELS: Record<MovementKind, string> = {
  Buy: 'Custo total',
  Sell: 'Valor líquido da venda',
  Dividend: 'Valor líquido recebido',
  Jcp: 'Valor líquido recebido',
  Split: '',
};

/** A stored figure as someone would type it back: `40,5`, and blank for zero. */
const typed = (value: number | undefined) => (value ? String(value).replace('.', ',') : '');

/**
 * Add or edit one movement (spec 007 UI). The fields follow the kind: Dividend and
 * Jcp take `amount` instead of quantity and price; a Split takes only the quantity,
 * since its price is zero by definition (decision 3) and the calculator ignores its
 * fees. The total is previewed live, exactly (`lib/decimal`), in the asset's currency.
 *
 * Nothing is validated here: the API's rules are the spec's table, and its messages
 * arrive in `errors` and are shown under the field they name.
 */
export default function MovementForm({
  currency,
  today,
  movement,
  errors = {},
  submitLabel,
  pending = false,
  onSubmit,
  onCancel,
}: {
  currency: string;
  /** The date a new movement opens on, `YYYY-MM-DD`. */
  today: string;
  movement?: Movement | undefined;
  errors?: Record<string, string[]>;
  submitLabel: string;
  pending?: boolean;
  onSubmit: (input: MovementInput) => void;
  onCancel?: () => void;
}) {
  const [kind, setKind] = useState<MovementKind>(movement?.kind ?? 'Buy');
  const [date, setDate] = useState(movement?.date ?? today);
  const [quantity, setQuantity] = useState(typed(movement?.quantity));
  const [unitPrice, setUnitPrice] = useState(typed(movement?.unitPrice));
  const [amount, setAmount] = useState(typed(movement?.amount));
  const [fees, setFees] = useState(typed(movement?.fees));
  const [notes, setNotes] = useState(movement?.notes ?? '');

  const income = kind === 'Dividend' || kind === 'Jcp';
  const split = kind === 'Split';
  const total = movementTotal(kind, { quantity, unitPrice, amount, fees });
  const elsewhere = Object.entries(errors)
    .filter(([field]) => !(FIELDS as readonly string[]).includes(field))
    .flatMap(([, messages]) => messages);

  return (
    <form
      aria-label="Movimentação"
      className="grid gap-4 sm:grid-cols-3"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit({
          date,
          kind,
          quantity: income ? 0 : toApiNumber(quantity),
          unitPrice: income || split ? 0 : toApiNumber(unitPrice),
          amount: income ? toApiNumber(amount) : 0,
          fees: split ? 0 : toApiNumber(fees),
          notes: notes.trim() === '' ? null : notes.trim(),
        });
      }}
    >
      <FormField id="movement-kind" label="Tipo" errors={errors.kind}>
        <select id="movement-kind" className={selectClasses} value={kind} onChange={(event) => setKind(event.target.value as MovementKind)}>
          {movementKinds.map((value) => (
            <option key={value} value={value}>
              {movementKindLabels[value]}
            </option>
          ))}
        </select>
      </FormField>
      <FormField id="movement-date" label="Data" errors={errors.date}>
        <Input id="movement-date" type="date" value={date} onChange={(event) => setDate(event.target.value)} />
      </FormField>
      {income ? (
        <FormField id="movement-amount" label="Valor recebido" errors={errors.amount}>
          <Input id="movement-amount" inputMode="decimal" value={amount} onChange={(event) => setAmount(event.target.value)} />
        </FormField>
      ) : (
        <FormField id="movement-quantity" label="Quantidade" errors={errors.quantity}>
          <Input id="movement-quantity" inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} />
        </FormField>
      )}
      {!income && !split && (
        <FormField id="movement-price" label="Preço unitário" errors={errors.unitPrice}>
          <Input id="movement-price" inputMode="decimal" value={unitPrice} onChange={(event) => setUnitPrice(event.target.value)} />
        </FormField>
      )}
      {!split && (
        <FormField id="movement-fees" label="Taxas" errors={errors.fees}>
          <Input id="movement-fees" inputMode="decimal" value={fees} onChange={(event) => setFees(event.target.value)} />
        </FormField>
      )}
      <FormField id="movement-notes" label="Observações (opcional)" errors={errors.notes}>
        <Input id="movement-notes" maxLength={300} value={notes} onChange={(event) => setNotes(event.target.value)} />
      </FormField>

      {!split && (
        <p data-testid="movement-total" className="text-sm sm:col-span-3" aria-live="polite">
          <span className="text-muted-foreground">{TOTAL_LABELS[kind]}: </span>
          <span className="font-medium tabular-nums">{total === null ? '—' : formatMoney(Number(total) / 100, currency)}</span>
        </p>
      )}

      {elsewhere.length > 0 && (
        <p role="alert" data-testid="movement-form-alert" className="text-destructive text-sm sm:col-span-3">
          {elsewhere.join(' ')}
        </p>
      )}

      <div className="flex gap-2 sm:col-span-3">
        <Button type="submit" disabled={pending}>
          {submitLabel}
        </Button>
        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel}>
            Cancelar
          </Button>
        )}
      </div>
    </form>
  );
}
