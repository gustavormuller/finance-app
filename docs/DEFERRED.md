# Deferred — questions, deviations, skipped items

Every entry: spec · checkpoint · what · why deferred · what was done instead.

## Run setup (orchestrator)

- **run · setup · `docs/CLAUDE_CODE_WORKFLOW.md` does not exist.** The run prompt says to read
  it. Nothing to read; proceeded with CLAUDE.md, ARCHITECTURE.md and the specs.
- **run · setup · `docs/handoffs/003.md` does not exist**, and neither did `docs/handoffs/`. The
  prompt says handoffs follow 003.md's shape. Shape taken from the prompt instead: status, what
  exists now, conventions, decisions the next spec must make, known debts, lessons.
- **run · setup · specs define no checkpoints.** The orchestrator split each spec into
  checkpoints along its own sections (domain + data model → application/API → web → E2E),
  keeping each near the ~200-line diff guideline. The split is listed per spec in STATUS.md.
- **run · setup · cloud Linux environment, not Windows.** .NET SDK 10.0.401 (pinned by
  global.json) installed from packages.microsoft.com's Debian 12 feed into /opt/dotnet,
  because builds.dotnet.microsoft.com is blocked by the egress proxy. Playwright expected
  Chromium build 1243; the preinstalled build 1194 was symlinked in its place. E2E runs against
  database `financas_e2e` via user-secrets (Google keys are dummies; E2E uses dev-login).
  None of this is committed — it is environment, not repo.
- **run · setup · orchestrator read the specs just-in-time.** ARCHITECTURE.md ADRs, 004 and
  005 were read up front; 006–010 are read when each spec starts, to keep the orchestrator's
  context small. Each subagent reads its own spec in full.

## 005 · checkpoint 1

- **005 · CP1 · unit test 3 ("Rule 3 with Transfer rejects zero") — ambiguity.** 003's
  design reports zero once, from rule 1 (`ValidateAmount`), and keeps rule 3 (`ValidateSign`)
  silent on zero so the `amount` field is not reported twice. Simplest reading kept: the test
  asserts `ValidateAmount(0)` fails and `ValidateSign(0, Transfer)` is null. A zero Transfer is
  still refused by every endpoint, which call both rules.
- **005 · CP1 · unit test 4 — no new test.** 003's unit test 7
  (`Sign_must_agree_with_the_category_kind`, all four Income/Expense combinations) already is
  test 4; its doc comment now says so. Unchanged and still green.
- **005 · CP1 · `openingBalance` on PUT — ambiguity.** Amendment 3 says "default 0". Chosen:
  optional (`decimal?`) on both routes; omitted on create → 0, omitted on update → the stored
  value is kept (same pattern as `currency`). Any sign accepted (a card starts negative).
  Rounded to 2 places `ToEven` like `Money`, so the response matches what `numeric(18,2)`
  stores. No upper-bound check: a value beyond `numeric(18,2)` would be a 500, as it already
  is for transaction amounts.
- **005 · CP1 · `docs/migrations/` did not exist.** Created it for `005-AddDashboard.sql`.
- **005 · CP1 · flaky test observed.** One full run failed
  `TransactionPersistenceTests` (two `Amount_round_trips_byte_identical` cases and the timezone
  test) before any implementation touched them; they passed in isolation and on every rerun.
  Not investigated further.
