# 019 — Market data that works without the owner's keys

## Goal

The market-data sync delivers what it can with no provider key at all: crypto priced in
reais comes from Binance's public API, and when a provider refuses because a key is not
configured, the sync run says so in pt-BR, naming the ticker and the setting to fill in. A
guide, `docs/market-data-keys.md`, tells the owner which keys exist, what each unlocks,
and how to set them.

## What was checked live on 2026-09-25

| Source | Without a key | Consequence |
|---|---|---|
| Binance `api/v3/klines` | `BTCBRL`, `ETHBRL`, `SOLBRL`, `USDTBRL` answer. An unknown symbol is HTTP 400 `{"code":-1121,"msg":"Invalid symbol."}`; a lowercase one is 400 `-1100`. `BTCBRL` history starts 2020-10-13. | a new provider, decisions 1–6 |
| brapi `quote/{ticker}` | `PETR4`, `VALE3`, `MGLU3`, `ITUB4` answer, `PETR4` with 5 years of history. `BBAS3`, `BOVA11`, `IVVB11` and an unknown ticker all answer HTTP 401 `{"error":true,"message":"Token de autenticação não fornecido","code":"MISSING_TOKEN"}`. | IVVB11, the S&P 500 benchmark, fails every run without a token |
| Twelve Data `time_series` | HTTP 401, body `{"code":401,"message":"**apikey** parameter is incorrect or not specified. …","status":"error"}` | no US stock works without a key |
| CoinGecko `market_chart` | works for `days` ≤ 365; `days=400` is HTTP 401 with `error_code` 10012 ("Public API users are limited to querying historical data within the past 365 days") | works keyless, capped by `MaxHistoryDays` 365 |
| BCB SGS 12, 11, 433, 1 | answer; latest values CDI 0.050788 (% a day), SELIC 0.050788 (% a day), IPCA −0.32 (% a month, 2026-08), USD 5.1795 (BRL per USD) | codes and units in `appsettings.json` are right |

What today's code does with those refusals: brapi's and Twelve Data's 401 become an
`HttpRequestException` with status 401, and `SyncErrorText` shows "O provedor recusou a
chave de acesso." That reads as a wrong key, when no key was sent. The `/market-data` page
already lists every failure of a run as "ticker: text", so the page needs no change; the
text does.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Which Binance data | Daily klines of a spot pair quoted in BRL: `GET {BaseUrl}klines?symbol=BTCBRL&interval=1d&startTime=…&endTime=…&limit=1000`. `MarketData:Binance:BaseUrl` = `https://api.binance.com/api/v3/`. No key, so no key setting. |
| 2 | Close and date | The close is element 4 of the candle, a JSON string, parsed to `decimal` with the invariant culture. The date is the UTC day of the open time (element 0, Unix ms). A daily candle closes at 23:59:59.999 UTC, so the close is that UTC day's last price. (CoinGecko's point dated D is the price at 00:00 UTC of D, which is one day earlier. The two series are not interchangeable.) |
| 3 | Paging | At most 1000 candles per request. The next page starts 1 ms after the last candle's open time. Paging stops at a page shorter than 1000 or a page that does not move forward. The 5-year backfill takes two requests. `endTime` is the last millisecond of `to`, so today's unfinished candle is never asked for. |
| 4 | Errors | 429 and 418 (Binance bans an IP that keeps going after a 429) are `ProviderRateLimitedException`. A 400 with code `-1121` (unknown symbol) is an empty series, like brapi's 404. Any other 400 is an `HttpRequestException` with Binance's code and message. The symbol is upper-cased before the request, because Binance refuses lowercase. |
| 5 | Wiring | `ProviderKind.Binance = 3`, registered like the others (typed client, the same per-provider resilience pipeline, `IPriceProviderRegistry`). `FakeMarketDataProviders` builds a fake for every `ProviderKind`, so it has a Binance fake with no change; a test pins that. |
| 6 | What may be registered as Binance | The currency must be `BRL` and the symbol must end in `BRL` (e.g. `BTCBRL`), so a USDT pair can never be stored as reais. The symbol is stored upper-cased. **(review)** |
| 7 | Migration | **None.** `MarketAssets.Provider` is an `int` with no check constraint, and `Currency` is `char(3)`, which already holds `BRL`. The model does not change: `dotnet ef migrations has-pending-model-changes` says so, and there is no SQL to review. |
| 8 | When a refusal means "no key" | brapi, Twelve Data and CoinGecko throw `ProviderKeyMissingException(provider, symbol, setting)` when they answer 401 or 403 **and their key setting is empty**. Twelve Data sends its error both as HTTP 401 and inside a 200 body (`code` 401), and both count. With a key configured, a 401/403 stays what it was: "O provedor recusou a chave de acesso." |
| 9 | The text | `SyncErrorText` composes it from the exception: "O brapi exige um token para BBAS3. Configure MarketData:Brapi:Token.", "O Twelve Data exige uma chave de API para AAPL. Configure MarketData:TwelveData:Key.", "O CoinGecko exige uma chave demo para bitcoin. Configure MarketData:CoinGecko:DemoKey." The setting is written in configuration form (`:`); the guide gives the environment form (`__`). The symbol is the one sent to the provider, so a CoinGecko asset shows its coin id. |
| 10 | The run | A missing key fails its item only. PETR4 still syncs when BBAS3 cannot, and the run is `PartialFailure`. Unlike a 429, it does not stop the provider for the rest of the run. |
| 11 | The page | Unchanged. It already renders each failure as "ticker: text" under the provider's line. A web unit test pins that the new text reaches the screen as sent. |
| 12 | Labels | `providerKindLabels.Binance = 'Binance'`. The registration select adds a short pt-BR description to each provider: "brapi (B3: ações, FIIs, ETFs)", "CoinGecko (cripto)", "Twelve Data (ações dos EUA)", "Binance (cripto em reais)". The catalogue and the run summary keep the plain name. **(review)** |
| 13 | CoinGecko demo key | Stays optional. It gives the app a monthly quota and a rate limit of its own (10,000 call credits a month on the pricing page), but **not more history**: the demo plan serves one year of daily data, the same 365 days as keyless. The guide says so. |

## Out of scope

- **brapi's plan limits with a token.** brapi's free plan serves "até 3 meses" of history
  (pricing page, 2026-09-25) and its docs say a request beyond the plan is HTTP 403.
  Startup serves 1 year, Pro more than 10. With a free token, a new asset's first sync asks
  for `range=5y`, gets a 403, and reads "O provedor recusou a chave de acesso.", every
  night. The fix needs a real free-plan response to test against (a configurable maximum
  range, or a clearer 403 text). It is left for when a token exists. **(review)**
- Binance pairs quoted in anything other than BRL, and Binance as a benchmark source
- A setting to prefer Binance over CoinGecko automatically: the provider is chosen per
  asset at registration, as today
- Geography: Binance answers HTTP 451 from restricted locations, the United States
  among them. The production host must not sit in one (the guide says so). The sync would
  show "Falha de comunicação com o provedor." for it.
- Replacing the hand-written fixtures of 006 with captures (only the new ones are captured)

## Data model changes

None (decision 7). `ProviderKind` gains `Binance = 3`, stored as the existing `int`.

## API surface

No new route. What changes:

```
POST /api/market-data/assets        and 007's POST /api/investments/assets
     { ticker, class, provider: "Binance", providerSymbol: "btcbrl", currency: "BRL" }
     201 { ..., provider: "Binance", providerSymbol: "BTCBRL", currency: "BRL" }
     400 currency       "Ativos da Binance são cotados em BRL."
     400 providerSymbol "Use um par da Binance cotado em reais, terminado em BRL (ex.: BTCBRL)."

GET  /api/market-data/sync-runs
     summary.Binance    a new key, as any provider
     failures[].error   may now read "O brapi exige um token para BBAS3. Configure MarketData:Brapi:Token."
```

## Configuration

```
MarketData:Binance:BaseUrl   https://api.binance.com/api/v3/
```

## UI behaviour

- Registration (`/market-data` and 007's "Adicionar ativo"): the provider select offers
  "Binance (cripto em reais)"; the symbol placeholder adds `BTCBRL`.
- `/market-data`: a Binance line in each run that synced a Binance asset; key-missing
  failures read as decision 9. No layout change.

## Test plan

### Unit — `api.tests/Unit`

**Binance, against a fixture captured on 2026-09-25 (`binance-klines-btcbrl.json`, five candles, BTCBRL 20–24 Sep):**
1. Klines parse to UTC days and `decimal` closes, and the request carries symbol, interval, `startTime`, `endTime`, `limit=1000`
2. A candle opening at 00:00 UTC is that day, and the day's last millisecond is still that day
3. A close with 26 significant digits reaches `decimal` intact (no `double`)
4. A 5-year range pages: a full page of 1000 leads to a second request starting 1 ms after the last open time; a short page ends it
5. A page that does not move forward ends the paging (no endless loop)
6. A lowercase symbol is sent upper-cased
7. Unknown symbol (captured `binance-klines-invalid-symbol.json`, HTTP 400 −1121) → empty
8. Another 400 → `HttpRequestException` carrying the status
9. 429 and 418 → `ProviderRateLimitedException` with the provider name `Binance`
10. Malformed bodies → `ProviderResponseInvalidException`, nothing else escapes

**Missing key:**
11. brapi, no token, the captured `MISSING_TOKEN` 401 → `ProviderKeyMissingException` (Brapi, BBAS3, `MarketData:Brapi:Token`)
12. brapi, token set, 401 → `HttpRequestException` 401, as before
13. Twelve Data, no key, the captured 401 → key missing; the same body under HTTP 200 → key missing
14. Twelve Data, key set, 401 → `HttpRequestException` 401, as before
15. CoinGecko, no demo key, 401 → key missing; with a key, 401 → `HttpRequestException`
16. `SyncErrorText` gives decision 9's sentence per provider, and a generic one for an unknown provider

**Wiring:**
17. The container resolves `ProviderKind.Binance` to `BinanceProvider`, and every kind has an adapter
18. The fakes answer for `Binance` like any kind
19. `appsettings.json` binds `MarketData:Binance:BaseUrl`

### Integration — `api.tests/Integration`

20. A sync with brapi refusing BBAS3 for a missing key and serving PETR4: PETR4 written, the run `PartialFailure`, the failure's text decision 9's
21. Registering a Binance asset: `BRL` + `btcbrl` → 201 with `BTCBRL`; `USD` → 400 `currency`; `BTCUSDT` → 400 `providerSymbol`

### Unit — `web`

22. The registration select offers "Binance (cripto em reais)" and posts `provider: "Binance"`
23. A run whose brapi failure is the missing-token text shows "BBAS3: O brapi exige um token para BBAS3. Configure MarketData:Brapi:Token."

### E2E

24. 006's test 26 also registers a Binance asset (`…BRL`); after the manual sync on the fakes, the run shows a Binance line and no failure

## End-to-end verification

```
bash scripts/verify.sh
```

E2E on the isolated ports (API :5094, Vite :5194, database `financas_e2e_market`), with
`MarketData__FakeProviders=true`.

Live, by hand, once: run the API with `MarketData__FakeProviders=false` against
`financas_e2e_market`, register `BTC` as Binance `BTCBRL`, trigger a sync, and check
`SELECT COUNT(*), MIN("Date"), MAX("Date") FROM "Prices"` for that asset: about 1826 rows
from five years ago to yesterday. With no brapi token, the same run shows IVVB11's failure
as "O brapi exige um token para IVVB11. Configure MarketData:Brapi:Token."

## Definition of done

- `verify.sh` green; E2E green on the isolated stack
- `docs/market-data-keys.md` written; ARCHITECTURE.md "External data sources" and
  "Environment variables" match the code; DEFERRED.md's "SGS codes unverified" item says
  they were verified on 2026-09-25
- The fixtures README lists the captured files as captured
