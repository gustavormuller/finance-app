import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Account, Category, ImportBatch, ImportBatchDetail, StagedRow } from '@/api/finance';
import { renderWithClient, stubFetch, type SeenRequest } from '@/test-utils';

import ImportPage from './ImportPage';

const categories: Category[] = [
  { id: 'cat-food', name: 'Alimentação', kind: 'Expense', parentId: null, createdAt: '' },
  { id: 'cat-other', name: 'Outros', kind: 'Expense', parentId: null, createdAt: '' },
];

const batch: ImportBatch = {
  id: 'batch-1',
  accountId: 'acc-1',
  accountName: 'Nubank',
  source: 'Ofx',
  fileName: 'extrato.ofx',
  status: 'Staged',
  rowCount: 1,
  committedCount: null,
  createdAt: '2026-09-18T00:00:00Z',
  committedAt: null,
};

function detail(row: Partial<StagedRow>): ImportBatchDetail {
  return {
    batch,
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

/** The API as the preview sees it; `suggest` answers the POST, and a 200 files row 1 under Alimentação. */
function stubApi(suggest: { status: number; body: unknown }) {
  let suggested = false;

  return stubFetch((request: SeenRequest) => {
    const path = new URL(request.url, 'http://localhost').pathname;

    if (request.method === 'POST' && path === '/api/imports/batch-1/suggest') {
      suggested = suggest.status === 200;
      return suggest;
    }

    switch (path) {
      case '/api/auth/me':
        return { body: { id: 'u1', email: 'ada@example.com', displayName: 'Ada', aiEnabled: true } };
      case '/api/accounts':
        return { body: [] };
      case '/api/categories':
        return { body: categories };
      case '/api/csv-templates':
        return { body: [] };
      case '/api/imports':
        return { body: [batch] };
      case '/api/imports/batch-1':
        return {
          body: suggested ? detail({ categoryId: 'cat-food', categorySource: 'Ai' }) : detail({}),
        };
      default:
        return undefined;
    }
  });
}

async function openPreview() {
  const user = userEvent.setup();
  renderWithClient(<ImportPage />);

  await user.click(await screen.findByRole('button', { name: 'Continuar' }));
  await screen.findByTestId('staged-row-Ready');

  const suggest = await screen.findByRole('button', { name: 'Sugerir com IA' });
  await waitFor(() => expect(suggest).toBeEnabled());

  return { user, suggest };
}

describe('ImportPage, "Sugerir com IA"', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('posts the suggestion, refetches the rows in place and says what changed', async () => {
    const seen = stubApi({ status: 200, body: { suggested: 1, skipped: 0 } });
    const { user, suggest } = await openPreview();

    await user.click(suggest);

    const row = screen.getByTestId('staged-row-Ready');
    expect(await within(row).findByTestId('ai-marker')).toBeInTheDocument();
    expect(within(row).getByRole('combobox', { name: 'Categoria da linha 1' })).toHaveValue('cat-food');
    expect(screen.getByRole('status')).toHaveTextContent('1 categoria sugerida pela IA.');
    expect(seen.filter((request) => request.method === 'POST').map((request) => request.url)).toEqual([
      '/api/imports/batch-1/suggest',
    ]);
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

describe('ImportPage, spreadsheets (011)', () => {
  const account: Account = {
    id: 'acc-1',
    name: 'Banco do Brasil',
    type: 'Checking',
    currency: 'BRL',
    createdAt: '',
    openingBalance: 0,
  };

  const sheetPreview = (amount: string) => ({
    headers: ['Data', 'Lançamento', 'Valor (R$)'],
    sampleRows: [['03/08/2026', 'Compra com Cartão', amount]],
    delimiter: null,
    skippedRows: 2,
    rowCount: 2,
  });

  function stubSheetApi() {
    return stubFetch((request: SeenRequest) => {
      const path = new URL(request.url, 'http://localhost').pathname;

      switch (path) {
        case '/api/auth/me':
          return { body: { id: 'u1', email: 'ada@example.com', displayName: 'Ada', aiEnabled: false } };
        case '/api/accounts':
          return { body: [account] };
        case '/api/categories':
          return { body: categories };
        case '/api/csv-templates':
          return { body: [] };
        case '/api/imports':
          return { body: [] };
        case '/api/imports/preview-csv': {
          const culture = previewField('culture');
          return { body: sheetPreview(culture === 'en-US' ? '-187.43' : '-187,43') };
        }
        default:
          return undefined;
      }
    });
  }

  /** The multipart field of the latest preview request; the stub only records JSON bodies. */
  function previewField(name: string): string | null {
    const calls = vi.mocked(fetch).mock.calls.filter(([url]) => String(url).endsWith('/api/imports/preview-csv'));
    const body = calls.at(-1)?.[1]?.body;

    return body instanceof FormData ? (body.get(name) as string | null) : null;
  }

  async function chooseSpreadsheet() {
    const user = userEvent.setup();
    renderWithClient(<ImportPage />);

    await screen.findByRole('option', { name: 'Banco do Brasil' });
    await user.selectOptions(screen.getByLabelText('Conta'), 'acc-1');
    await user.upload(screen.getByLabelText('Arquivo'), new File([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], 'extrato.xlsx'));
    await user.click(screen.getByRole('button', { name: 'Enviar' }));
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

    await waitFor(() => expect(previewField('culture')).toBe('en-US'));
    expect(await screen.findByText('-187.43')).toBeInTheDocument();
  });
});
