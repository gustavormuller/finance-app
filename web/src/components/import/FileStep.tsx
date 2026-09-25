import { Upload } from 'lucide-react';
import { useState } from 'react';

import Alert from '@/components/Alert';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

/**
 * Step 1: a file for the account the page is about (015), dropped on the zone or
 * chosen with the button, and either starts at once. The extension decides where the
 * file goes next — `.ofx` straight to the preview, `.csv`, `.xls` and `.xlsx` to the
 * mapping step — and that decision is the caller's, so this component only hands the
 * file over.
 */
export default function FileStep({
  accountName,
  busy,
  error,
  onFile,
}: {
  accountName: string;
  busy: boolean;
  error: React.ReactNode;
  onFile: (file: File) => void;
}): React.JSX.Element {
  const [dragging, setDragging] = useState(false);

  return (
    <div className="grid gap-4">
      <div
        data-testid="drop-zone"
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(event) => {
          event.preventDefault();
          setDragging(false);
          const file = event.dataTransfer.files[0];

          if (file && !busy) {
            onFile(file);
          }
        }}
        className={cn(
          'border-primary/35 bg-primary/5 flex flex-col items-center gap-2.5 rounded-2xl border-2 border-dashed px-4 py-8 text-center transition-colors sm:px-8',
          dragging && 'border-primary bg-accent',
        )}
      >
        <span className="bg-accent text-primary flex size-13 items-center justify-center rounded-2xl">
          <Upload className="size-6" aria-hidden="true" />
        </span>
        <p className="font-display text-xl font-semibold break-words">Arraste o extrato do {accountName} aqui</p>
        <p className="text-muted-foreground max-w-md text-sm">
          OFX, CSV ou planilha do Excel (.xls, .xlsx), até 2 MB e 5.000 lançamentos. Lançamentos repetidos ficam
          de fora sozinhos.
        </p>

        {/* The input is the control, visually replaced by its label; the peer ring shows
            where keyboard focus is. Cleared after each pick, so the same file can be
            chosen again after a refusal. */}
        <input
          id="import-file"
          type="file"
          accept=".ofx,.csv,.txt,.xls,.xlsx"
          className="peer sr-only"
          disabled={busy}
          onChange={(event) => {
            const file = event.target.files?.[0];
            event.target.value = '';

            if (file) {
              onFile(file);
            }
          }}
        />
        <Button
          asChild
          className={cn(
            'peer-focus-visible:ring-ring/50 mt-1.5 cursor-pointer peer-focus-visible:ring-[3px]',
            busy && 'pointer-events-none opacity-50',
          )}
        >
          <label htmlFor="import-file">{busy ? 'Enviando…' : 'Escolher arquivo'}</label>
        </Button>
        <p className="text-muted-foreground text-xs">
          Um OFX vai direto para a revisão; CSV e planilhas passam antes pelo mapeamento das colunas.
        </p>
      </div>

      {error && <Alert>{error}</Alert>}
    </div>
  );
}
