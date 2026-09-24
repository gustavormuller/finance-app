# 009 — AI analysis

## Goal

Two things, on two cost profiles. Categorisation as the last rung of ADR-012's cascade — cheap model, called from the import preview. Monthly analysis — better model, generated in the background, rendered on the dashboard. Both behind `ai_enabled` and a hard per-user budget.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | Provider port | `IAiProvider` with `Anthropic` and `OpenAi` implementations, config-selected. A `Fake` for tests. ADR-015 satisfied: two real implementations. |
| 2 | Model names and prices | **Configuration.** Model identifiers and per-token prices change; they are never in code. |
| 3 | Budget enforcement | `ai_usage` rows per call. A middleware sums the user's month before every call and refuses at the cap. **Usage is recorded even when the call fails** — input tokens were spent. |
| 4 | Categorisation trigger | An explicit button in 004's preview: *"Sugerir com IA"*. Synchronous, 30 s timeout. Only rows that fell to the sign-default (not history) are sent. |
| 5 | Categorisation cache | **None needed.** Once the user commits, rung 2 (history) catches the same description next time. |
| 6 | Analysis execution | **Async.** 10–30 s is too long for a request. `POST` creates a `Pending` row; a `BackgroundService` consumes an in-process `Channel<T>`; the row is the state. On startup, any `Pending` row older than 5 minutes is re-enqueued. That is enough durability for this scale. No Hangfire. |
| 7 | What the analysis sees | **Aggregates, not raw rows.** Per-category totals for the last 3 months, balances, top 20 merchants by spend, month-over-month deltas, portfolio summary from 007. No descriptions beyond merchant names. |
| 8 | Prompt storage | Files in `Infrastructure/Ai/Prompts/*.md`, versioned in git. Output language pt-BR, structure fixed by the prompt. |
| 9 | MCP server | **Out — becomes 011.** It needs token-based auth for a second client, which deserves its own spec. |
| 10 | Privacy | Sending financial aggregates to a third-party API is opt-in via `ai_enabled`. What is sent is documented in the UI beside the toggle. |

### Correction to existing docs

**ADR-003** — final form: scheduled jobs use `BackgroundService` + Cronos (006); on-demand jobs use `BackgroundService` + `Channel<T>` with a database row as durable state (009). Hangfire not adopted. Revisit only if a job needs retries with backoff across process restarts.

## Out of scope

- MCP server — 011
- Chat or free-form questions in the app — the cost profile that motivated the MCP split
- Rung 1 of the cascade (user-defined rules)
- Any AI on the investments side beyond the summary line in the analysis
- Fine-tuning, embeddings, RAG
- An `ai_enabled` UI toggle for other users — the user's own toggle only

## Data model changes

Migration: `AddAi`. Review the SQL.

### `AiUsage : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | |
| `Month` | `char(7)` | `YYYY-MM` |
| `Purpose` | `int` | `Categorisation`, `Analysis` |
| `Provider` | `varchar(20)` | |
| `Model` | `varchar(100)` | |
| `InputTokens` | `int` | |
| `OutputTokens` | `int` | |
| `CostBrl` | `numeric(10,4)` | |
| `Succeeded` | `bool` | |
| `CreatedAt` | `timestamptz` | |

Index `(UserId, Month)`.

### `AiAnalysis : IUserOwned`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | |
| `UserId` | `uuid` | |
| `Month` | `char(7)` | the month analysed |
| `Status` | `int` | `Pending`, `Running`, `Completed`, `Failed` |
| `Content` | `text NULL` | markdown, pt-BR |
| `Error` | `varchar(500) NULL` | |
| `PromptVersion` | `varchar(20)` | git-tracked file's version header |
| `CreatedAt` | `timestamptz` | |
| `StartedAt` | `timestamptz NULL` | |
| `CompletedAt` | `timestamptz NULL` | |

Unique `(UserId, Month)` — one analysis per month; regenerating replaces.

### `AppUser.AiEnabled`

Exists since 002. 009 adds `PATCH /api/auth/me { aiEnabled }` so the user can toggle their own.

## Configuration

```
Ai:Provider                     anthropic | openai
Ai:MonthlyBudgetBrl             15.00
Ai:Categorisation:Model         <cheap model id>
Ai:Analysis:Model               <better model id>
Ai:Pricing:<model>:InputPerMTokUsd
Ai:Pricing:<model>:OutputPerMTokUsd
Ai:Anthropic:ApiKey
Ai:OpenAi:ApiKey
Ai:UsdBrl                       fallback rate for pricing if the benchmark is missing
```

Cost per call `= (input / 1e6) × inputPrice + (output / 1e6) × outputPrice`, prices in BRL derived from USD list prices at the latest `USDBRL` benchmark (006), or the fallback.

Prices are configured in USD, as the providers list them (`…PerMTokUsd`): decided by the user in 009 · CP2.

## Domain and application

### `IAiProvider` — `Application/Ai/`

```csharp
public interface IAiProvider
{
    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct);
}
public sealed record AiRequest(string Model, string System, string User, int MaxTokens);
public sealed record AiCompletion(string Text, int InputTokens, int OutputTokens);
```

### `BudgetGuard` — `Application/Ai/`

```
EnsureWithinBudget(userId, month):
  spent = SUM(CostBrl) for (userId, month)
  if spent >= budget → throw AiBudgetExceeded
```

Called before every provider call. Recorded after every call, success or not.

### `CategorisationCascade` — rung 3

Input: staged rows that fell to the default, plus the user's categories. One request: a JSON array of `{ rowId, description }` and the category list. Output expected: JSON `{ rowId: categoryId }`.

Tolerant parsing: strip code fences, ignore unknown row ids, ignore category ids the user does not own, ignore mismatched kind against the row's sign. Anything unparseable → the row keeps its default. **The AI can only suggest; rule 3 and the user's confirmation still apply.**

### `AnalysisInputBuilder`

Assembles the aggregates from decision 7 into a JSON document. Unit-tested so the shape is stable across prompt revisions.

### `AnalysisJob`

1. Dequeue `(userId, analysisId)`
2. Set `Running`, `StartedAt`
3. `BudgetGuard`
4. Build input, load prompt, call provider
5. Record `AiUsage`
6. Set `Completed` with content, or `Failed` with error

One `try` around 3–4; `AiUsage` is written in `finally` when a call was attempted.

## API surface

```
PATCH /api/auth/me                              200 { aiEnabled }

POST  /api/imports/{id}/suggest                 200 { suggested: n, skipped: n }
      403 ai_enabled false · 402 budget exceeded · 504 provider timeout

POST  /api/ai/analyses                          202 { analysisId } — { month }
      403 · 402
GET   /api/ai/analyses/{id}                     200 status + content
GET   /api/ai/analyses?month=                   200 the user's analyses
GET   /api/ai/usage?month=                      200 { spentBrl, budgetBrl, calls }
```

`402 Payment Required` is used deliberately for the budget cap — it is the one status that says exactly this.

## UI behaviour

**Settings** — `/settings`, new route, in the nav. The `ai_enabled` toggle with a plain pt-BR explanation of what is sent to the provider (decision 7) and to whom. Below it, this month's spend against the budget.

**Import preview (004)** — a *"Sugerir com IA"* button when `ai_enabled`, disabled with a reason otherwise. Rows updated in place with a small indicator that the suggestion came from AI.

**Dashboard (005)** — an *"Análise do mês"* card. If `Completed`, render the markdown. If `Pending`/`Running`, a spinner and polling every 3 s. If none, a *"Gerar análise"* button. A *"Regenerar"* action when one exists.

## Prompt — `Infrastructure/Ai/Prompts/monthly-analysis.md`

Version header on line 1. Instructs: pt-BR; sections *Resumo*, *Onde o dinheiro foi*, *O que mudou*, *Investimentos*, *Sugestões*; concrete numbers from the input only; no invented figures; at most 400 words; no preamble.

The prompt is reviewed like code. Changing it bumps the version.

## Test plan

### Unit — `api.tests/Unit`

**Budget:**
1. Spent 14.99 of 15 → allowed
2. Spent 15.00 → refused
3. Cost calculation with known token counts and prices → exact `decimal`
4. Cost uses the latest `USDBRL` when present, fallback otherwise

**Categorisation parsing:**
5. Clean JSON → all rows mapped
6. Fenced JSON → still parsed
7. Unknown row id → ignored
8. Category id not owned by user → ignored
9. Category kind mismatching sign → ignored
10. Garbage → no rows changed, no exception

**Analysis input:**
11. Shape is stable and contains no raw descriptions
12. Three months of aggregates, month-over-month deltas correct

### Integration — `api.tests/Integration`, with `FakeAiProvider`

**Isolation — mandatory:**
13. A's analyses and usage invisible to B
14. B `GET /api/ai/analyses/{A's id}` → `404`

**Gates:**
15. `ai_enabled = false` → `403` on both endpoints, no usage row
16. Budget exceeded → `402`, no provider call
17. Provider failure → usage row with `Succeeded = false` and input tokens recorded

**Categorisation:**
18. Rows with history suggestions are not sent; default rows are
19. Fake returns a mapping → staged rows updated, others unchanged
20. Fake returns garbage → nothing changes, `200` with `suggested: 0`

**Analysis lifecycle:**
21. `POST` → `202`, row `Pending`; job runs → `Completed` with content
22. Fake throws → `Failed` with error, usage recorded
23. Second `POST` for the same month → replaces, one row remains
24. Startup sweep: a `Pending` row older than 5 min is re-enqueued

### Unit — `web`

25. Toggle off → suggest button disabled with reason
26. Analysis card polls while `Pending`, stops on `Completed`
27. Markdown renders; script tags in content are escaped

### E2E

28. Enable AI → import → suggest → rows change (fake provider)
29. Generate analysis → card shows content

## End-to-end verification

```
bash scripts/verify.sh
bash scripts/verify-e2e.sh
```

Manual, with a real key and a small budget:

1. Set `Ai:MonthlyBudgetBrl` to `1.00`
2. Enable AI in settings; read the disclosure text as if you were a family member
3. Import a real statement, press *Sugerir com IA* — check the suggestions against your own judgement
4. Generate the month's analysis; read it critically — does every number in it appear in your data?
5. Check `/api/ai/usage` — cost is plausible against the provider's console
6. Keep generating until `402` appears — confirm it does
7. Set the budget back to `15.00`

Step 4 is the one that matters: an analysis that invents a number is worse than no analysis.

## Definition of done

- Both verify scripts green
- Manual steps 1–7 behave as described; the budget cap fires
- Test 17 passes — failed calls still cost and are recorded
- Test 27 passes — markdown from the provider cannot inject script
- Prompt has a version header and is reviewed
- ADR-003 amended to its final form
