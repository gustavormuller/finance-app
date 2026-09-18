import { useState } from 'react';

import { Button } from '@/components/ui/button';

/**
 * Undo is the only destructive action in the app so far, so it asks first — and the
 * question names the row count, because "desfazer?" tells you nothing about what is
 * about to disappear and "excluir 312 lançamentos?" does.
 *
 * Inline rather than `window.confirm`: testable, styled like the rest, and the
 * count is right there next to the button that caused it.
 */
export default function UndoButton({
  count,
  onUndo,
  disabled = false,
  size = 'default',
}: {
  count: number;
  onUndo: () => unknown;
  disabled?: boolean;
  size?: 'default' | 'sm';
}): React.JSX.Element {
  const [confirming, setConfirming] = useState(false);

  if (!confirming) {
    return (
      <Button
        type="button"
        variant="outline"
        size={size}
        disabled={disabled}
        onClick={() => setConfirming(true)}
      >
        Desfazer
      </Button>
    );
  }

  return (
    <span role="alertdialog" className="inline-flex flex-wrap items-center gap-2 text-sm">
      <span>
        Desfazer esta importação e excluir {count} {count === 1 ? 'lançamento' : 'lançamentos'}?
      </span>
      <Button
        type="button"
        variant="destructive"
        size={size}
        disabled={disabled}
        onClick={async () => {
          await onUndo();
          setConfirming(false);
        }}
      >
        Confirmar
      </Button>
      <Button type="button" variant="ghost" size={size} onClick={() => setConfirming(false)}>
        Cancelar
      </Button>
    </span>
  );
}
