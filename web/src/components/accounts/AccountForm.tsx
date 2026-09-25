import { useState } from 'react';

import type { Account, AccountInput, AccountType } from '@/api/finance';
import { selectClasses } from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { parseOpeningBalance, typedOpeningBalance } from '@/lib/accounts';
import { accountTypeLabels, accountTypes } from '@/lib/labels';

/**
 * Name, type, currency and opening balance: the one form that creates an account and,
 * in its details tab, edits it. `children` are extra buttons beside the submit.
 */
export default function AccountForm({
  account,
  submitLabel,
  busy,
  errors,
  onSubmit,
  children,
}: {
  account?: Account | undefined;
  submitLabel: string;
  busy: boolean;
  errors: Record<string, string[]>;
  onSubmit: (input: AccountInput) => void;
  children?: React.ReactNode;
}): React.JSX.Element {
  // Refused here rather than sent: the API would only answer with its own 400.
  const [notNumber, setNotNumber] = useState(false);

  return (
    <form
      className="grid gap-4 sm:grid-cols-2"
      onSubmit={(event) => {
        event.preventDefault();
        const form = new FormData(event.currentTarget);
        const openingBalance = parseOpeningBalance(String(form.get('openingBalance') ?? ''));

        setNotNumber(!Number.isFinite(openingBalance));

        if (!Number.isFinite(openingBalance)) {
          return;
        }

        onSubmit({
          name: String(form.get('name') ?? ''),
          type: String(form.get('type') ?? 'Checking') as AccountType,
          currency: String(form.get('currency') ?? 'BRL').toUpperCase(),
          openingBalance,
        });
      }}
    >
      <div className="grid gap-2">
        <Label htmlFor="account-name">Nome</Label>
        <Input id="account-name" name="name" defaultValue={account?.name ?? ''} required />
        <FieldError messages={errors.name} />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="account-type">Tipo</Label>
        <select id="account-type" name="type" className={selectClasses} defaultValue={account?.type ?? 'Checking'}>
          {accountTypes.map((type) => (
            <option key={type} value={type}>
              {accountTypeLabels[type]}
            </option>
          ))}
        </select>
        <FieldError messages={errors.type} />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="account-currency">Moeda</Label>
        <Input
          id="account-currency"
          name="currency"
          maxLength={3}
          className="uppercase"
          defaultValue={account?.currency ?? 'BRL'}
        />
        <FieldError messages={errors.currency} />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="account-opening-balance">Saldo inicial</Label>
        <Input
          id="account-opening-balance"
          name="openingBalance"
          inputMode="decimal"
          placeholder="0,00"
          className="amount tabular-nums"
          defaultValue={account ? typedOpeningBalance(account.openingBalance) : ''}
        />
        <FieldError messages={notNumber ? ['Informe um número.'] : errors.openingBalance} />
      </div>

      <div className="flex flex-wrap gap-2 sm:col-span-2">
        <Button type="submit" disabled={busy}>
          {submitLabel}
        </Button>
        {children}
      </div>
    </form>
  );
}

function FieldError({ messages }: { messages?: string[] | undefined }) {
  return messages && messages.length > 0 ? (
    <p role="alert" className="text-destructive text-sm">
      {messages.join(' ')}
    </p>
  ) : null;
}
