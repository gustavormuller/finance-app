import { useState } from 'react';

import type { Account } from '@/api/finance';
import Alert from '@/components/Alert';
import { selectClasses } from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

/**
 * Step 1: pick an account, pick a file. The extension decides where the file goes
 * next — `.ofx` straight to the preview, `.csv`, `.xls` and `.xlsx` to the mapping
 * step — and that decision is the page's, so this component only hands the file over.
 */
export default function FileStep({
  accounts,
  busy,
  error,
  onSubmit,
}: {
  accounts: Account[];
  busy: boolean;
  error: React.ReactNode;
  onSubmit: (accountId: string, file: File) => void;
}): React.JSX.Element {
  const [accountId, setAccountId] = useState(accounts[0]?.id ?? '');
  const [file, setFile] = useState<File | null>(null);
  const [touched, setTouched] = useState(false);

  const missingAccount = touched && accountId === '';
  const missingFile = touched && file === null;

  return (
    <form
      noValidate
      className="grid max-w-xl gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        setTouched(true);

        if (accountId !== '' && file !== null) {
          onSubmit(accountId, file);
        }
      }}
    >
      <div className="grid gap-2">
        <Label htmlFor="import-account">Conta</Label>
        <select
          id="import-account"
          className={selectClasses}
          value={accountId}
          onChange={(event) => setAccountId(event.target.value)}
        >
          <option value="">Escolha uma conta</option>
          {accounts.map((account) => (
            <option key={account.id} value={account.id}>
              {account.name}
            </option>
          ))}
        </select>
        {missingAccount && (
          <p role="alert" className="text-destructive text-sm">
            Escolha a conta que recebe os lançamentos.
          </p>
        )}
      </div>

      <div className="grid gap-2">
        <Label htmlFor="import-file">Arquivo</Label>
        <Input
          id="import-file"
          type="file"
          accept=".ofx,.csv,.txt,.xls,.xlsx"
          onChange={(event) => setFile(event.target.files?.[0] ?? null)}
        />
        <p className="text-muted-foreground text-sm">
          OFX, CSV ou planilha do Excel (.xls, .xlsx) exportada do banco, até 2 MB e 5.000
          lançamentos. Um OFX vai direto para a revisão; CSV e planilhas passam antes pelo
          mapeamento das colunas.
        </p>
        {missingFile && (
          <p role="alert" className="text-destructive text-sm">
            Escolha um arquivo.
          </p>
        )}
      </div>

      {error && <Alert>{error}</Alert>}

      <div>
        <Button type="submit" disabled={busy}>
          {busy ? 'Enviando…' : 'Enviar'}
        </Button>
      </div>
    </form>
  );
}
