import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { Movement } from '@/api/finance';
import MovementForm from './MovementForm';

const plain = (text: string | null) => (text ?? '').replace(/\u00a0/g, ' ');

function renderForm(props: Partial<React.ComponentProps<typeof MovementForm>> = {}) {
  const onSubmit = vi.fn();
  render(<MovementForm currency="BRL" today="2026-09-24" submitLabel="Registrar" onSubmit={onSubmit} {...props} />);

  return onSubmit;
}

describe('MovementForm', () => {
  /** Spec web unit test 28. */
  it('shows amount for a dividend and hides quantity and price', async () => {
    renderForm();

    expect(screen.getByLabelText('Quantidade')).toBeInTheDocument();
    expect(screen.getByLabelText('Preço unitário')).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor recebido')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Dividend');

    expect(screen.getByLabelText('Valor recebido')).toBeInTheDocument();
    expect(screen.queryByLabelText('Quantidade')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Preço unitário')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Jcp');
    expect(screen.getByLabelText('Valor recebido')).toBeInTheDocument();
    expect(screen.queryByLabelText('Quantidade')).not.toBeInTheDocument();
  });

  it('asks only for the quantity of a split, which moves no money', async () => {
    renderForm();
    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Split');

    expect(screen.getByLabelText('Quantidade')).toBeInTheDocument();
    expect(screen.queryByLabelText('Preço unitário')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Taxas')).not.toBeInTheDocument();
    expect(screen.queryByTestId('movement-total')).not.toBeInTheDocument();
  });

  /** Spec web unit test 29. */
  it('updates the total preview with quantity, price and fees', async () => {
    renderForm();
    const total = () => plain(screen.getByTestId('movement-total').textContent);

    expect(total()).toContain('—');

    await userEvent.type(screen.getByLabelText('Quantidade'), '100');
    await userEvent.type(screen.getByLabelText('Preço unitário'), '32,1234');
    expect(total()).toContain('R$ 3.212,34');

    await userEvent.type(screen.getByLabelText('Taxas'), '5');
    expect(total()).toContain('R$ 3.217,34');

    await userEvent.clear(screen.getByLabelText('Quantidade'));
    await userEvent.type(screen.getByLabelText('Quantidade'), '10');
    expect(total()).toContain('R$ 326,23');

    // A sell's fees come out of the proceeds.
    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Sell');
    expect(total()).toContain('R$ 316,23');
  });

  it('writes the total in the asset’s own currency', async () => {
    renderForm({ currency: 'USD' });
    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Dividend');
    await userEvent.type(screen.getByLabelText('Valor recebido'), '12,5');

    expect(plain(screen.getByTestId('movement-total').textContent)).toContain('US$ 12,50');
  });

  it('submits the figures as numbers, with the fields the kind does not use at zero', async () => {
    const onSubmit = renderForm();

    await userEvent.type(screen.getByLabelText('Quantidade'), '100');
    await userEvent.type(screen.getByLabelText('Preço unitário'), '32,12');
    await userEvent.type(screen.getByLabelText('Taxas'), '4,90');
    await userEvent.click(screen.getByRole('button', { name: 'Registrar' }));

    expect(onSubmit).toHaveBeenLastCalledWith({
      date: '2026-09-24',
      kind: 'Buy',
      quantity: 100,
      unitPrice: 32.12,
      amount: 0,
      fees: 4.9,
      notes: null,
    });

    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Dividend');
    await userEvent.type(screen.getByLabelText('Valor recebido'), '120');
    await userEvent.type(screen.getByLabelText('Observações (opcional)'), 'Provento de setembro');
    await userEvent.clear(screen.getByLabelText('Data'));
    await userEvent.type(screen.getByLabelText('Data'), '2026-09-15');
    await userEvent.click(screen.getByRole('button', { name: 'Registrar' }));

    expect(onSubmit).toHaveBeenLastCalledWith({
      date: '2026-09-15',
      kind: 'Dividend',
      quantity: 0,
      unitPrice: 0,
      amount: 120,
      fees: 4.9,
      notes: 'Provento de setembro',
    });
  });

  it('opens on the movement being edited', () => {
    const movement: Movement = {
      id: 'm1',
      assetId: 'a1',
      date: '2026-09-10',
      kind: 'Sell',
      quantity: 50,
      unitPrice: 40.5,
      amount: 0,
      fees: 4.9,
      currency: 'BRL',
      notes: 'Metade',
      createdAt: '2026-09-10T12:00:00Z',
    };
    renderForm({ movement });

    expect(screen.getByLabelText('Tipo')).toHaveValue('Sell');
    expect(screen.getByLabelText('Data')).toHaveValue('2026-09-10');
    expect(screen.getByLabelText('Quantidade')).toHaveValue('50');
    expect(screen.getByLabelText('Preço unitário')).toHaveValue('40,5');
    expect(screen.getByLabelText('Taxas')).toHaveValue('4,9');
    expect(screen.getByLabelText('Observações (opcional)')).toHaveValue('Metade');
  });

  it('shows each API message under the field it names, and any other in an alert', () => {
    renderForm({
      errors: {
        quantity: ['Quantidade vendida maior que a posição'],
        date: ['Data fora do intervalo'],
        currency: ['Moeda diferente do ativo'],
      },
    });

    const quantity = screen.getByLabelText('Quantidade').closest('div')!;
    expect(within(quantity).getByText('Quantidade vendida maior que a posição')).toBeInTheDocument();
    const date = screen.getByLabelText('Data').closest('div')!;
    expect(within(date).getByText('Data fora do intervalo')).toBeInTheDocument();
    expect(screen.getByTestId('movement-form-alert')).toHaveTextContent('Moeda diferente do ativo');
  });
});
