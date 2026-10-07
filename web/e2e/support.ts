import { fileURLToPath } from 'node:url';

import { expect, type Locator, type Page } from '@playwright/test';

/**
 * Establishes a session through the development-only login endpoint, because
 * Playwright cannot complete a real Google flow.
 *
 * Issued as a fetch from inside the page rather than through Playwright's request
 * context, so the browser is the one doing it: it sends the Origin header the API's
 * CSRF check requires, and it stores the Secure session cookie under the same rules
 * the real callback relies on.
 */
export async function devLogin(page: Page, email: string, displayName: string) {
  await page.goto('/login');

  const status = await page.evaluate(
    async (account) => {
      const response = await fetch('/api/auth/dev-login', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(account),
      });

      return response.status;
    },
    { email, displayName },
  );

  expect(status).toBe(204);
}

/** The dev database persists across runs, so every test brings its own account. */
export function uniqueEmail(prefix: string) {
  return `${prefix}-${crypto.randomUUID()}@example.com`;
}

/**
 * An account by way of the real screen, so every spec starts from a state a person could reach.
 * `openingBalance` is typed as a person would, e.g. `1.000,00`; omitted, the field stays empty (0).
 * `type` is the enum member (`Checking` unless given) and `currency` the typed code (BRL unless given).
 */
export async function createAccount(
  page: Page,
  name: string,
  openingBalance?: string,
  { type = 'Checking', currency }: { type?: string; currency?: string } = {},
) {
  await page.goto('/accounts');
  // Settled first, as a person sees it: with accounts, /accounts opens the first one. Creating
  // one before that is the race `accounts.spec.ts` holds the list back to drive (spec 027, bug 7).
  await expect(
    page.getByRole('button', { name: 'Criar a primeira conta' }).or(page.getByRole('navigation', { name: 'Seções da conta' })),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await page.getByLabel('Nome').fill(name);
  // By value, not label: the option reads "Conta corrente" but the wire contract is
  // still the English enum member.
  await page.getByLabel('Tipo').selectOption(type);
  if (currency !== undefined) {
    await page.getByLabel('Moeda').fill(currency);
  }
  if (openingBalance !== undefined) {
    await page.getByLabel('Saldo inicial').fill(openingBalance);
  }
  await page.getByRole('button', { name: 'Criar conta' }).click();

  // A new account is selected, so its name becomes the detail's heading.
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
}

/**
 * An account's "Importar extrato" tab, reached the way a person reaches it: the
 * accounts, the account's card, the tab. Waits for the wizard's first step.
 */
export async function openImportTab(page: Page, account: string) {
  await page.goto('/accounts');
  await page.getByRole('link', { name: account, exact: true }).click();
  await expect(page.getByRole('heading', { name: account, exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Importar extrato' }).click();
  await expect(page.getByRole('heading', { name: '1. Arquivo' })).toBeVisible();
}

/** A file under ./fixtures, as a path Playwright can upload. */
export function fixture(name: string) {
  return fileURLToPath(new URL(`./fixtures/${name}`, import.meta.url));
}

/**
 * Chooses a statement on the account's import tab, which starts the upload: a fixture by
 * name, or a file made by the test. The caller waits for the step it expects next.
 */
export async function uploadStatement(
  page: Page,
  account: string,
  file: string | { name: string; mimeType: string; buffer: Buffer },
) {
  await openImportTab(page, account);
  await page.getByLabel('Escolher arquivo').setInputFiles(typeof file === 'string' ? fixture(file) : file);
}

/**
 * An OFX statement in reais, made by the test for the files a fixture would hold only
 * once: one with a marked row, one too long, one with no transactions. `date` is
 * `YYYY-MM-DD` and `amount` is written as the bank writes it, `-55.90`.
 */
export function ofxStatement(name: string, rows: { date: string; amount: string; id: string; memo: string }[]) {
  const transactions = rows.map((row) =>
    [
      '<STMTTRN>',
      '<TRNTYPE>OTHER',
      `<DTPOSTED>${row.date.replaceAll('-', '')}000000[-3:BRT]`,
      `<TRNAMT>${row.amount}`,
      `<FITID>${row.id}`,
      `<MEMO>${row.memo}`,
      '</STMTTRN>',
    ].join('\n'),
  );
  const text = [
    'OFXHEADER:100',
    'DATA:OFXSGML',
    'VERSION:102',
    '',
    '<OFX>',
    '<BANKMSGSRSV1><STMTTRNRS><STMTRS>',
    '<CURDEF>BRL',
    '<BANKTRANLIST>',
    ...transactions,
    '</BANKTRANLIST>',
    '</STMTRS></STMTTRNRS></BANKMSGSRSV1>',
    '</OFX>',
  ].join('\n');

  return { name, mimeType: 'application/x-ofx', buffer: Buffer.from(text, 'utf8') };
}

/** The import's current step; the history underneath offers the same verbs for other batches. */
export function importStep(page: Page): Locator {
  return page.getByTestId('import-step');
}

/** Confirms the batch in review and waits for the done step. */
export async function commitImport(page: Page) {
  await importStep(page).getByRole('button', { name: 'Confirmar importação' }).click();
  await expect(page.getByRole('heading', { name: '4. Concluído' })).toBeVisible();
}

/**
 * Narrows the transactions list to a date range. The list opens on the current month,
 * and the fixtures are fixed dates, so a test that looks for its rows shows their month
 * first rather than passing only while the calendar is on it.
 */
export async function showTransactionsBetween(page: Page, from: string, to: string) {
  await page.getByLabel('De', { exact: true }).fill(from);
  await page.getByLabel('Até', { exact: true }).fill(to);
}

/**
 * A transaction by way of the real form. `direction` answers the Saída/Entrada question
 * the form asks only for a Transfer category.
 */
export async function createTransaction(
  page: Page,
  values: {
    account: string;
    category: string;
    amount: string;
    date: string;
    description: string;
    direction?: 'Saída' | 'Entrada';
  },
) {
  await page.goto('/transactions');
  await page.getByRole('button', { name: 'Novo lançamento' }).click();

  // Exact, because getByLabel matches substrings and the filter bar on this same
  // page is labelled "Filtrar por conta" and "Filtrar por categoria".
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: values.account });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: values.category });
  if (values.direction) {
    await page.getByRole('radio', { name: values.direction, exact: true }).check();
  }
  await page.getByLabel('Valor').fill(values.amount);
  await page.getByLabel('Data').fill(values.date);
  await page.getByLabel('Descrição').fill(values.description);

  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  // Wait for the form to close, which it only does once the POST has succeeded.
  // Returning straight after the click lets the next navigation abort the request
  // in flight, and the row silently never exists.
  await expect(page.getByRole('button', { name: 'Criar lançamento' })).toBeHidden();
}

/**
 * A manual sync, so an asset has closes before its buy: a buy with no close yet has no
 * daily row until the next sync. Posted from the page so the browser sends the Origin
 * header the API's CSRF check requires, then polled until the run has finished. A 429 is
 * waited out: the rebuild after the previous run's sync can still hold the gate for a
 * moment after that run reads `Succeeded`.
 */
export async function syncMarketData(page: Page) {
  const syncRunId = await page.evaluate(async () => {
    for (let attempt = 0; attempt < 60; attempt++) {
      const response = await fetch('/api/market-data/sync', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
      });
      if (response.status === 202) {
        return ((await response.json()) as { syncRunId: string }).syncRunId;
      }
      if (response.status !== 429) {
        throw new Error(`sync answered ${response.status}`);
      }
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
    throw new Error('the sync gate stayed busy for 30 s');
  });

  await expect
    .poll(
      () =>
        page.evaluate(async (id) => {
          const response = await fetch('/api/market-data/sync-runs', { credentials: 'same-origin' });
          const runs = (await response.json()) as { id: string; status: string }[];
          return runs.find((run) => run.id === id)?.status;
        }, syncRunId),
      { timeout: 15_000 },
    )
    .toBe('Succeeded');
}

/** A ticker no earlier run registered: the market-data catalogue is shared and never emptied. */
export function uniqueTicker(prefix: string) {
  return `${prefix}${crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}`;
}

/** `days` before today in UTC, the API's calendar, as `YYYY-MM-DD`. */
export function utcDaysAgo(days: number) {
  return new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10);
}

/** Today on the browser's calendar, `YYYY-MM-DD`: what the dashboard calls the current month. */
export function localToday() {
  return localMonthDay(0, new Date().getDate());
}

/**
 * Day `day` of the month `monthsBack` months before the browser's current one,
 * `YYYY-MM-DD`. The 15th by default, which is inside that month whatever today is.
 */
export function localMonthDay(monthsBack: number, day = 15) {
  const date = new Date();
  date.setDate(1);
  date.setMonth(date.getMonth() - monthsBack);

  return [date.getFullYear(), date.getMonth() + 1, day].map((part) => String(part).padStart(2, '0')).join('-');
}

/**
 * Holds back the next `method` request whose path is `path`, as a slow network would, until the
 * returned function is called; the request reaches the API only then. Every other request
 * passes. Spec 028 lands a save late this way, after the person has opened another form.
 */
export async function holdBack(page: Page, method: string, path: string | RegExp): Promise<() => void> {
  let release: () => void = () => {};
  const held = new Promise<void>((resolve) => (release = resolve));
  let holding = true;

  await page.route(
    (url) => (typeof path === 'string' ? url.pathname === path : path.test(url.pathname)),
    async (route) => {
      if (holding && route.request().method() === method) {
        holding = false;
        await held;
        await route.continue();
        return;
      }

      await route.fallback();
    },
  );

  return release;
}

/**
 * Pages the dashboard's month selector back to `month` (`YYYY-MM`), labelled `label` as
 * the selector writes it, so a test on fixed dates holds whatever the current month is.
 * The selector opens on the browser's month, so that is the clock read here.
 */
export async function showDashboardMonth(page: Page, month: string, label: string) {
  const selected = page.getByTestId('selected-month');
  await expect(selected).toBeVisible();

  const current = await page.evaluate(() => {
    const now = new Date();
    return now.getFullYear() * 12 + now.getMonth();
  });
  const [year, number] = month.split('-').map(Number);
  const back = current - (year! * 12 + number! - 1);
  expect(back, 'the month must not be in the future').toBeGreaterThanOrEqual(0);

  for (let i = 0; i < back; i++) {
    await page.getByRole('button', { name: 'Mês anterior' }).click();
  }

  await expect(selected).toHaveText(label);
}
