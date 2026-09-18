import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import UndoButton from './UndoButton';

describe('UndoButton', () => {
  /** Spec web unit test 62. The question names what is about to be deleted. */
  it('asks for confirmation naming the row count before undoing', async () => {
    const onUndo = vi.fn();
    const user = userEvent.setup();

    render(<UndoButton count={312} onUndo={onUndo} />);

    await user.click(screen.getByRole('button', { name: 'Desfazer' }));

    expect(onUndo).not.toHaveBeenCalled();
    expect(screen.getByRole('alertdialog')).toHaveTextContent('excluir 312 lançamentos?');

    await user.click(screen.getByRole('button', { name: 'Confirmar' }));

    expect(onUndo).toHaveBeenCalledTimes(1);
  });

  it('cancelling leaves everything as it was', async () => {
    const onUndo = vi.fn();
    const user = userEvent.setup();

    render(<UndoButton count={1} onUndo={onUndo} />);

    await user.click(screen.getByRole('button', { name: 'Desfazer' }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('excluir 1 lançamento?');

    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(onUndo).not.toHaveBeenCalled();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Desfazer' })).toBeInTheDocument();
  });
});
