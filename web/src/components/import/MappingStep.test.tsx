import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { CsvPreview } from '@/api/finance';

import MappingStep from './MappingStep';

const preview: CsvPreview = {
  headers: ['Data', 'Valor', 'Descrição'],
  sampleRows: [['03/04/2026', '-1.234,56', 'PAG*IFOOD']],
  delimiter: ';',
  skippedRows: 0,
  rowCount: 2,
};

function renderStep() {
  const onSubmit = vi.fn();

  render(
    <MappingStep
      preview={preview}
      templates={[]}
      delimiter=";"
      busy={false}
      error={null}
      onDelimiterChange={vi.fn()}
      onBack={vi.fn()}
      onSubmit={onSubmit}
    />,
  );

  return onSubmit;
}

describe('MappingStep', () => {
  /**
   * Spec web unit test 58. The same cell, 03/04/2026, reads as 3 April under one
   * format and 4 March under the other, and the preview says so before anything is
   * uploaded.
   */
  it('re-renders the preview when the date format changes', async () => {
    const user = userEvent.setup();
    renderStep();

    await user.selectOptions(screen.getByLabelText('Coluna de data'), 'Data');
    await user.selectOptions(screen.getByLabelText('Coluna de valor'), 'Valor');
    await user.click(screen.getByLabelText('Descrição'));

    const previewBody = screen.getByTestId('mapping-preview');
    expect(within(previewBody).getByText('3 abr 2026')).toBeInTheDocument();
    expect(within(previewBody).getByText('−1.234,56')).toBeInTheDocument();
    expect(within(previewBody).getByText('PAG*IFOOD')).toBeInTheDocument();

    const format = screen.getByLabelText('Formato da data');
    await user.clear(format);
    await user.type(format, 'MM/dd/yyyy');

    expect(within(previewBody).getByText('4 mar 2026')).toBeInTheDocument();
    expect(within(previewBody).queryByText('3 abr 2026')).not.toBeInTheDocument();

    // And a format the cell cannot fit is an issue on the row, never a guess.
    await user.clear(format);
    await user.type(format, 'yyyy-MM-dd');

    expect(within(previewBody).getByText('Data inválida: "03/04/2026"')).toBeInTheDocument();
  });

  it('submits the mapping with the description columns in the order they were chosen', async () => {
    const user = userEvent.setup();
    const onSubmit = renderStep();

    expect(screen.getByRole('button', { name: 'Continuar' })).toBeDisabled();

    await user.selectOptions(screen.getByLabelText('Coluna de data'), 'Data');
    await user.selectOptions(screen.getByLabelText('Coluna de valor'), 'Valor');
    await user.click(screen.getByLabelText('Descrição'));
    await user.click(screen.getByLabelText('Data', { selector: 'input[type=checkbox]' }));
    await user.type(screen.getByLabelText('Salvar como modelo (opcional)'), 'Meu banco');

    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(onSubmit).toHaveBeenCalledWith(
      {
        delimiter: ';',
        hasHeader: true,
        culture: 'pt-BR',
        dateFormat: 'dd/MM/yyyy',
        signMode: 'Signed',
        dateColumn: 'Data',
        amountColumn: 'Valor',
        debitColumn: null,
        creditColumn: null,
        descriptionColumns: 'Descrição, Data',
      },
      'Meu banco',
    );
  });

  it('switching to debit and credit asks for both columns', async () => {
    const user = userEvent.setup();
    renderStep();

    await user.selectOptions(screen.getByLabelText('Sinal'), 'DebitCredit');

    expect(screen.getByLabelText('Coluna de débito')).toBeInTheDocument();
    expect(screen.getByLabelText('Coluna de crédito')).toBeInTheDocument();
    expect(screen.queryByLabelText('Coluna de valor')).not.toBeInTheDocument();
  });
});
