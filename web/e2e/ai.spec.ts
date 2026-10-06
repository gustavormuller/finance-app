import { expect, test, type Page } from '@playwright/test';

import {
  commitImport,
  createAccount,
  devLogin,
  importStep,
  ofxStatement,
  showDashboardMonth,
  uniqueEmail,
  uploadStatement,
} from './support';

/**
 * Spec 009 E2E tests 28 and 29, and 024's AI and settings flows, against the real API and a
 * real PostgreSQL, with the API on `Ai:FakeProvider` (scripts/verify-e2e.sh): no request
 * leaves the machine.
 *
 * Every test signs in as a brand-new user, so AI starts off, nothing is shared between tests
 * or runs, and there is no state to put back. No test syncs market data, so all run in the
 * `chromium` project.
 */

/** The month `extrato.ofx`'s rows fall in, as the month selector shows it. */
const FIXTURE_MONTH = { key: '2026-09', label: 'setembro de 2026' };

const AI_SWITCH = 'Usar IA nesta conta';

/** Turns the user's AI on or off through the real switch, and waits until it is saved. */
async function switchAi(page: Page, on: boolean) {
  await page.goto('/settings');

  const state = page.getByTestId('ai-state');
  await expect(state).toHaveText(on ? 'Desligada' : 'Ligada');
  await page.getByRole('switch', { name: AI_SWITCH }).setChecked(on);
  await expect(state).toHaveText(on ? 'Ligada' : 'Desligada');
  // The new state shows while the change is still being saved, and the switch is disabled
  // until it is. Leaving the page before that cancels the request (a 499), and the
  // account keeps its old setting.
  await expect(page.getByRole('switch', { name: AI_SWITCH })).toBeEnabled();
}

/** Step 1 through to the review, for `extrato.ofx`. */
async function uploadOfx(page: Page, account: string) {
  await uploadStatement(page, account, 'extrato.ofx');
  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();
}

/** Spec E2E test 28; 024: asking again finds nothing left on the default, and makes no call. */
test('with AI on, "Sugerir com IA" changes the preview rows in place (fake provider)', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-suggest'), 'Ada Lovelace');
  await switchAi(page, true);
  await createAccount(page, 'Nubank');

  await uploadOfx(page, 'Nubank');

  // A new user has no history, so every row starts on its sign's default category.
  const firstRow = page.getByRole('combobox', { name: 'Categoria da linha 1' });
  await expect(firstRow.locator('option:checked')).toHaveText('Outros');
  await expect(page.getByTestId('ai-marker')).toHaveCount(0);

  await importStep(page).getByRole('button', { name: 'Sugerir com IA' }).click();

  await expect(importStep(page).getByRole('status')).toHaveText('3 categorias sugeridas pela IA.');
  await expect(page.getByTestId('ai-marker')).toHaveCount(3);
  // The fake answers each row's kind with its first category that is not a default.
  await expect(firstRow.locator('option:checked')).toHaveText('Alimentação');
  await expect(
    page.getByRole('row', { name: /EMPRESA LTDA/ }).getByRole('combobox').locator('option:checked'),
  ).toHaveText('Salário');

  await importStep(page).getByRole('button', { name: 'Sugerir com IA' }).click();
  await expect(importStep(page).getByRole('status')).toHaveText('Nenhuma linha na categoria padrão para a IA sugerir.');

  // The one call is on the account's bill; asking with nothing to send cost nothing.
  await page.getByRole('link', { name: 'Configurações' }).click();
  await expect(page.getByTestId('ai-spend')).toContainText('1 chamada');
});

/**
 * 024: the fake answers a request holding a "GARBAGE" row with prose instead of the
 * categories (spec 009 test 20), as a confused model might. Nothing moves, and the call
 * still counts, because the provider charges for it.
 */
test('an answer the AI gets wrong leaves every row on its default', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-garbage'), 'Grace Hopper');
  await switchAi(page, true);
  await createAccount(page, 'Nubank');
  await uploadStatement(
    page,
    'Nubank',
    ofxStatement('confuso.ofx', [
      { date: '2026-09-02', amount: '-12.00', id: 'garbage-1', memo: 'Loja Garbage' },
      { date: '2026-09-03', amount: '-30.00', id: 'garbage-2', memo: 'Padaria Central' },
    ]),
  );
  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();

  await importStep(page).getByRole('button', { name: 'Sugerir com IA' }).click();

  await expect(importStep(page).getByRole('status')).toHaveText(
    'A IA não sugeriu nenhuma categoria. 2 linhas continuaram na categoria padrão.',
  );
  await expect(page.getByTestId('ai-marker')).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Categoria da linha 2' }).locator('option:checked')).toHaveText('Outros');

  await page.getByRole('link', { name: 'Configurações' }).click();
  await expect(page.getByTestId('ai-spend')).toContainText('1 chamada');
});

/** Spec E2E test 29; 024: Regenerar writes the month's analysis again. */
test('"Gerar análise" on the dashboard fills the card with the month\'s analysis (fake provider)', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-analysis'), 'Grace Hopper');
  await switchAi(page, true);
  await createAccount(page, 'Inter');

  await uploadOfx(page, 'Inter');
  await commitImport(page);

  await page.goto('/');
  await showDashboardMonth(page, FIXTURE_MONTH.key, FIXTURE_MONTH.label);

  const card = page.getByTestId('analysis-card');
  await expect(card).toContainText('Nenhuma análise de setembro de 2026 ainda.');
  await card.getByRole('button', { name: 'Gerar análise' }).click();

  // The card polls every 3 s while the row is Pending or Running.
  const content = card.getByTestId('analysis-content');
  await expect(content).toBeVisible({ timeout: 15_000 });
  for (const heading of ['Resumo', 'Onde o dinheiro foi', 'O que mudou', 'Investimentos', 'Sugestões']) {
    await expect(content.getByRole('heading', { name: heading })).toBeVisible();
  }
  // The fake quotes the month and its expense from what was sent: 55,90 + 1.234,56.
  await expect(content).toContainText(`Análise de teste de ${FIXTURE_MONTH.key}`);
  await expect(content).toContainText('As saídas do mês somaram R$ 1290.46.');
  await expect(card.getByRole('button', { name: 'Regenerar' })).toBeEnabled();

  const regenerating = page.waitForResponse(
    (response) => response.url().endsWith('/api/ai/analyses') && response.request().method() === 'POST',
  );
  await card.getByRole('button', { name: 'Regenerar' }).click();
  expect((await regenerating).status()).toBe(202);
  // The job makes the call after the 202; its usage row is the sign it ran.
  await expect
    .poll(() => page.evaluate(async () => ((await (await fetch('/api/ai/usage')).json()) as { calls: number }).calls), {
      timeout: 15_000,
    })
    .toBe(2);
  await expect(content).toContainText(`Análise de teste de ${FIXTURE_MONTH.key}`, { timeout: 15_000 });
  await expect(card.getByRole('button', { name: 'Regenerar' })).toBeEnabled({ timeout: 15_000 });

  // Kept: the month's analysis is read back, not generated, on the next visit.
  await page.reload();
  await showDashboardMonth(page, FIXTURE_MONTH.key, FIXTURE_MONTH.label);
  await expect(content).toContainText(`Análise de teste de ${FIXTURE_MONTH.key}`);

  await page.getByRole('link', { name: 'Configurações' }).click();
  await expect(page.getByTestId('ai-spend')).toContainText('2 chamadas');
});

/** 024: with AI off, both of its buttons stay on screen, disabled, saying where to turn it on. */
test('with AI off, "Sugerir com IA" and "Gerar análise" are disabled and say why', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-off'), 'Alan Turing');
  await createAccount(page, 'Nubank');

  await uploadOfx(page, 'Nubank');
  const suggest = importStep(page).getByRole('button', { name: 'Sugerir com IA' });
  await expect(suggest).toBeDisabled();
  await expect(suggest).toHaveAccessibleDescription(
    'A IA está desligada na sua conta. Ligue-a em Configurações para receber sugestões.',
  );

  await page.goto('/');
  const generate = page.getByTestId('analysis-card').getByRole('button', { name: 'Gerar análise' });
  await expect(generate).toBeDisabled();
  await expect(generate).toHaveAccessibleDescription(
    'A IA está desligada na sua conta. Ligue-a em Configurações para gerar a análise.',
  );
});

/** 024: the switch is the user's own, saved on the server, with what it sends spelled out. */
test('the AI switch is saved both ways, beside its spend and what it sends', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-switch'), 'Katherine Johnson');
  await page.goto('/settings');

  await expect(page.getByTestId('ai-spend')).toHaveText(/^R\$\s0,00 de R\$\s15,00 em [a-zç]+ de \d{4} · 0 chamadas$/);
  const disclosure = page.getByTestId('ai-disclosure');
  for (const heading of ['O que é enviado ao provedor de IA', 'Sugerir com IA, na importação', 'Análise do mês, no painel']) {
    await expect(disclosure.getByRole('heading', { name: heading })).toBeVisible();
  }

  await switchAi(page, true);
  await page.reload();
  await expect(page.getByTestId('ai-state')).toHaveText('Ligada');
  await expect(page.getByRole('switch', { name: AI_SWITCH })).toBeChecked();

  await switchAi(page, false);
  await page.reload();
  await expect(page.getByTestId('ai-state')).toHaveText('Desligada');
  await expect(page.getByRole('switch', { name: AI_SWITCH })).not.toBeChecked();
});

/**
 * 024: a second tab turns AI off while this one still shows it on. The API checks the
 * switch on every call, so the request is refused with the sentence it sends.
 */
test('AI switched off in another tab: the suggestion is refused with the reason', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-other-tab'), 'Ada Lovelace');
  await switchAi(page, true);
  await createAccount(page, 'Nubank');
  await uploadOfx(page, 'Nubank');
  await expect(importStep(page).getByRole('button', { name: 'Sugerir com IA' })).toBeEnabled();

  const other = await page.context().newPage();
  await switchAi(other, false);
  await other.close();

  await importStep(page).getByRole('button', { name: 'Sugerir com IA' }).click();
  await expect(importStep(page).getByRole('alert')).toHaveText(
    'A IA está desligada na sua conta. Ligue-a nas configurações para usar este recurso.',
  );
  await expect(page.getByTestId('ai-marker')).toHaveCount(0);
});
