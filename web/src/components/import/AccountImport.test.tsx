import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { ImportBatchDetail, StagedRow } from '@/api/finance';
import { account, batch, renderAt, stubAccountsApi } from '@/routes/accounts-fixtures';
import type { SeenRequest } from '@/test-utils';

/**
 * The import inside an account (015): the wizard of 004 and 011, scoped to the
 * account in the address, at `/accounts/$accountId?tab=import`.
 */

const nubank = account('acc-1', 'Nubank');

const staged = batch('batch-1', nubank, {
  fileName: 'extrato.ofx',
  status: 'Staged',
  rowCount: 1,
  committedCount: null,
  committedAt: null,
});

function detail(row: Partial<StagedRow>): ImportBatchDetail {
  return {
    batch: staged,
    counts: { ready: 1, duplicates: 0, invalid: 0, included: 1 },
    rows: {
      items: [
        {
          id: 'r1',
          rowNumber: 1,
          date: '2026-09-10',
          amount: -42.9,
          currency: 'BRL',
          rawDescription: 'PAG*IFOOD 10/09',
          externalId: null,
          categoryId: 'cat-other',
          categorySource: 'Default',
          status: 'Ready',
          included: true,
          issues: [],
          ...row,
        },
      ],
      page: 1,
      pageSize: 100,
      total: 1,
    },
  };
}

/** A multipart field of the latest POST to `path`; the stub only records JSON bodies. */
function formField(path: string, name: string): string | null {
  const calls = vi
    .mocked(fetch)
    .mock.calls.filter(([url, init]) => init?.method === 'POST' && new URL(String(url), 'http://localhost').pathname === path);
  const body = calls.at(-1)?.[1]?.body;

  return body instanceof FormData ? (body.get(name) as string | null) : null;
}

const posts = (seen: SeenRequest[]) => seen.filter((request) => request.method === 'POST').map((request) => request.url);

describe('AccountImport, the file step', () => {
  afterEach(() => vi.unstubAllGlobals());

  function stubUpload() {
    return stubAccountsApi({ accounts: [account('acc-0', 'Itaú'), nubank] }, (request, url) => {
      if (request.method === 'POST' && url.pathname === '/api/imports') {
        return { status: 201, body: { batchId: 'batch-1', rowCount: 1, ready: 1, duplicates: 0, invalid: 0 } };
      }

      return url.pathname === '/api/imports/batch-1' ? { body: detail({}) } : undefined;
    });
  }

  const ofx = () => new File(['<OFX></OFX>'], 'extrato.ofx');

  /** Spec 015 test 9. */
  it('has no account picker, names the account, and uploads the file to it', async () => {
    const user = userEvent.setup();
    const seen = stubUpload();
    renderAt('/accounts/acc-1?tab=import');

    expect(await screen.findByText('Arraste o extrato do Nubank aqui')).toBeInTheDocument();
    expect(screen.getByText(/OFX, CSV ou planilha do Excel/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Conta')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Enviar' })).not.toBeInTheDocument();
    expect(screen.getByTestId('import-step')).not.toHaveAttribute('data-wide');

    await user.upload(screen.getByLabelText('Escolher arquivo'), ofx());

    expect(await screen.findByTestId('staged-row-Ready')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '3. Revisão' })).toBeInTheDocument();
    // Spec 015 decision 14: the review asks for the page's width.
    expect(screen.getByTestId('import-step')).toHaveAttribute('data-wide');
    expect(posts(seen)).toEqual(['/api/imports']);
    expect(formField('/api/imports', 'accountId')).toBe('acc-1');
    expect(formField('/api/imports', 'source')).toBe('Ofx');
  });

  /** Spec 015 test 10. */
  it('starts the same upload when a file is dropped on the zone', async () => {
    const seen = stubUpload();
    renderAt('/accounts/acc-1?tab=import');

    fireEvent.drop(await screen.findByTestId('drop-zone'), { dataTransfer: { files: [ofx()], types: ['Files'] } });

    expect(await screen.findByTestId('staged-row-Ready')).toBeInTheDocument();
    expect(posts(seen)).toEqual(['/api/imports']);
    expect(formField('/api/imports', 'accountId')).toBe('acc-1');
  });
});

describe('AccountImport, the history and the one statement in review', () => {
  afterEach(() => vi.unstubAllGlobals());

  const itau = account('acc-0', 'Itaú');

  /** Spec 015 test 11. */
  it("lists only this account's imports", async () => {
    stubAccountsApi({
      accounts: [itau, nubank],
      imports: [batch('b-nubank', nubank, { fileName: 'nubank-setembro.ofx' }), batch('b-itau', itau, { fileName: 'itau-setembro.ofx' })],
    });
    renderAt('/accounts/acc-1?tab=import');

    const rows = await screen.findAllByTestId('import-history-row');
    expect(rows).toHaveLength(1);
    expect(rows[0]).toHaveTextContent('nubank-setembro.ofx');
    expect(screen.queryByText('itau-setembro.ofx')).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Conta' })).not.toBeInTheDocument();
  });

  it('says when this account has no imports yet', async () => {
    stubAccountsApi({ accounts: [itau, nubank], imports: [batch('b-itau', itau)] });
    renderAt('/accounts/acc-1?tab=import');

    expect(await screen.findByText('Nenhuma importação nesta conta ainda.')).toBeInTheDocument();
  });

  /** Spec 015 test 12. */
  it("points to the other account when that account's statement is in review", async () => {
    const user = userEvent.setup();
    stubAccountsApi({
      accounts: [itau, nubank],
      imports: [batch('b-itau', itau, { fileName: 'itau.ofx', status: 'Staged', committedCount: null, committedAt: null })],
    });
    const router = renderAt('/accounts/acc-1?tab=import');

    expect(await screen.findByText(/Há um extrato em revisão na conta Itaú\./)).toBeInTheDocument();
    expect(screen.queryByTestId('drop-zone')).not.toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Abrir a importação da conta Itaú' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/accounts/acc-0'));
    expect(router.state.location.search).toEqual({ tab: 'import' });
    expect(await screen.findByText('O extrato itau.ofx ainda está em revisão.')).toBeInTheDocument();
  });

  /** Spec 015 test 13. */
  it("resumes this account's statement in review instead of taking another", async () => {
    const user = userEvent.setup();
    stubAccountsApi({ accounts: [nubank], imports: [staged] }, (_request, url) =>
      url.pathname === '/api/imports/batch-1' ? { body: detail({}) } : undefined,
    );
    renderAt('/accounts/acc-1?tab=import');

    expect(await screen.findByText('O extrato extrato.ofx ainda está em revisão.')).toBeInTheDocument();
    expect(screen.queryByTestId('drop-zone')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Continuar a revisão' }));

    expect(await screen.findByTestId('staged-row-Ready')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '3. Revisão' })).toBeInTheDocument();
  });
});

describe('AccountImport, "Sugerir com IA"', () => {
  afterEach(() => vi.unstubAllGlobals());

  /** The API as the preview sees it; `suggest` answers the POST, and a 200 files row 1 under Alimentação. */
  function stubApi(suggest: { status: number; body: unknown }) {
    let suggested = false;

    return stubAccountsApi({ accounts: [nubank], imports: [staged] }, (request, url) => {
      if (request.method === 'POST' && url.pathname === '/api/imports/batch-1/suggest') {
        suggested = suggest.status === 200;
        return suggest;
      }

      return url.pathname === '/api/imports/batch-1'
        ? { body: suggested ? detail({ categoryId: 'cat-food', categorySource: 'Ai' }) : detail({}) }
        : undefined;
    });
  }

  async function openPreview() {
    const user = userEvent.setup();
    renderAt('/accounts/acc-1?tab=import');

    const history = await screen.findByTestId('import-history-row');
    await user.click(within(history).getByRole('button', { name: 'Continuar' }));
    await screen.findByTestId('staged-row-Ready');

    const suggest = await screen.findByRole('button', { name: 'Sugerir com IA' });
    await waitFor(() => expect(suggest).toBeEnabled());

    return { user, suggest };
  }

  /** Spec 015 test 14 (009's, moved from the import page). */
  it('posts the suggestion, refetches the rows in place and says what changed', async () => {
    const seen = stubApi({ status: 200, body: { suggested: 1, skipped: 0 } });
    const { user, suggest } = await openPreview();

    await user.click(suggest);

    const row = screen.getByTestId('staged-row-Ready');
    expect(await within(row).findByTestId('ai-marker')).toBeInTheDocument();
    expect(within(row).getByRole('combobox', { name: 'Categoria da linha 1' })).toHaveValue('cat-food');
    expect(screen.getByRole('status')).toHaveTextContent('1 categoria sugerida pela IA.');
    expect(posts(seen)).toEqual(['/api/imports/batch-1/suggest']);
  });

  it.each([
    [403, 'IA desligada', 'A IA está desligada na sua conta. Ligue-a nas configurações para usar este recurso.'],
    [402, 'Limite de IA atingido', 'Você atingiu o limite mensal de gastos com IA. O limite renova no próximo mês.'],
    [504, 'A IA demorou demais', 'O serviço de IA não respondeu a tempo. Tente novamente em instantes.'],
    [502, 'Falha no serviço de IA', 'O serviço de IA não conseguiu responder agora. Tente novamente mais tarde.'],
  ])('renders a %i problem\'s detail verbatim and leaves the rows as they were', async (status, title, text) => {
    stubApi({ status, body: { status, title, detail: text } });
    const { user, suggest } = await openPreview();

    await user.click(suggest);

    expect(await screen.findByRole('alert')).toHaveTextContent(text);
    expect(within(screen.getByTestId('staged-row-Ready')).queryByTestId('ai-marker')).not.toBeInTheDocument();
  });
});

describe('AccountImport, spreadsheets (011)', () => {
  const bb = account('acc-1', 'Banco do Brasil');

  const sheetPreview = (amount: string) => ({
    headers: ['Data', 'Lançamento', 'Valor (R$)'],
    sampleRows: [['03/08/2026', 'Compra com Cartão', amount]],
    delimiter: null,
    skippedRows: 2,
    rowCount: 2,
  });

  function stubSheetApi() {
    return stubAccountsApi({ accounts: [bb] }, (_request, url) =>
      url.pathname === '/api/imports/preview-csv'
        ? { body: sheetPreview(formField('/api/imports/preview-csv', 'culture') === 'en-US' ? '-187.43' : '-187,43') }
        : undefined,
    );
  }

  async function chooseSpreadsheet() {
    const user = userEvent.setup();
    renderAt('/accounts/acc-1?tab=import');

    await user.upload(
      await screen.findByLabelText('Escolher arquivo'),
      new File([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], 'extrato.xlsx'),
    );
    await screen.findByLabelText('Formato dos números');

    return user;
  }

  afterEach(() => vi.unstubAllGlobals());

  /** Spec 011 web test 16. */
  it('sends an .xlsx to the mapping step, without a delimiter field', async () => {
    stubSheetApi();
    await chooseSpreadsheet();

    expect(screen.queryByLabelText('Delimitador')).not.toBeInTheDocument();
    expect(screen.getByText(/Células de data e número da planilha/)).toBeInTheDocument();
    expect(screen.getByText('-187,43')).toBeInTheDocument();
  });

  /** Spec 011 web test 17. */
  it('asks for the preview again when the number format changes', async () => {
    stubSheetApi();
    const user = await chooseSpreadsheet();

    await user.selectOptions(screen.getByLabelText('Formato dos números'), 'en-US');

    await waitFor(() => expect(formField('/api/imports/preview-csv', 'culture')).toBe('en-US'));
    expect(await screen.findByText('-187.43')).toBeInTheDocument();
  });
});
