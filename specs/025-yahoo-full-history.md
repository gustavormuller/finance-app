# 025 — Yahoo Finance as the primary price source, full history, no keys

## Goal

Prices come first from Yahoo Finance's chart endpoint, which needs no key and reaches back
decades: B3 stocks and funds, US stocks, indices, crypto in dollars and exchange rates, each
with a total-return series beside the traded close. The BCB benchmarks reach back to the Plano
Real, so a fresh install syncs everything, with full history, from Yahoo, BCB and Binance
alone.

The owner asked for it on 2026-10-06, for his own use only: "Quero poder comparar ação do Itaú
com dólar, por exemplo. Bitcoin USD com WEGE3." The comparison page itself is 026.

## What was checked live on 2026-10-06

From this machine, with no cookie, no crumb and a desktop browser's `User-Agent`:

```
GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}
    ?period1=-2208988800&period2={now}&interval=1d&events=div%2Csplit&includeAdjustedClose=true
```

| Symbol | Currency | First day | Days | Notes |
|---|---|---|---|---|
| ITUB4.SA | BRL | 2000-12-21 | 6465 | 276 dividends; 11 splits, the last two bonus shares 110:100 (2025-03-18) and 103:100 (2025-12-26) |
| WEGE3.SA | BRL | 2000-01-03 | 6718 | splits 13:10 (2018-04-25) and 2:1 (2021-04-28) |
| PETR4.SA | BRL | 2000-01-03 | 6718 | |
| HGLG11.SA | BRL | 2011-03-02 | 3876 | 57 dividends: Yahoo's FII dividends are incomplete |
| IVVB11.SA | BRL | 2014-04-29 | 3096 | no split, no dividend; the 2026-10-05 close was still null at 03:00 UTC on the 6th |
| ^BVSP | BRL | 1993-04-27 | 8462 | Ibovespa |
| ^GSPC | USD | 1927-12-30 | 24807 | S&P 500 |
| ^IXIC | USD | 1971-02-05 | 14033 | Nasdaq Composite |
| AAPL | USD | 1980-12-12 | 11545 | splits 2:1 ×3, 7:1, 4:1 (2020-08-31); 92 dividends |
| VOO | USD | 2010-09-09 | 4042 | |
| BTC-USD | USD | 2014-09-17 | 4403 | the 2026-10-05 close null on the 6th |
| ETH-USD | USD | 2017-11-09 | 3254 | |
| SOL-USD | USD | 2020-04-10 | 2371 | |
| BRL=X (= USDBRL=X) | BRL | 2003-12-01 | 5963 | 453 null closes |
| EURBRL=X | BRL | 2003-12-01 | 5963 | |
| GC=F | USD | 2000-08-30 | 6633 | gold futures |
| BTC-BRL | — | — | — | HTTP 404 |
| IFIX.SA, ^IFIX | — | — | — | 422 and 404: Yahoo has no IFIX |

What the responses look like, and what the adapter must do about it:

- `meta` carries `currency`, `exchangeTimezoneName` (`America/Sao_Paulo`, `America/New_York`,
  `UTC` for crypto, `Europe/London` for FX), `gmtoffset` (the offset **now**, in seconds),
  `priceHint` (2 for stocks and indices, 4 for FX) and `firstTradeDate`.
- A bar is stamped at its session's start: 13:00 UTC for B3, 13:30 or 14:30 UTC for New
  York, 00:00 UTC for crypto, and **London midnight** for FX: 23:00 UTC the day before in
  summer, 00:00 UTC in winter. A fixed offset dates winter or summer FX bars a day off.
- `indicators.quote[0].close` is **split-adjusted retroactively**: AAPL on 2020-08-28 reads
  124.8075 where it traded at 499.23 before the 4:1 split. `indicators.adjclose[0].adjclose`
  is adjusted for splits and dividends (ITUB4 on 2026-09-30: close 44.28, adjclose 44.2618,
  the day before a dividend).
- `events.splits` and `events.dividends` hold only events inside `[period1, period2]`.
- The latest bar, or today's, can be null; FX adds one more point stamped "now". 
- Unknown symbol: HTTP 404 `{"chart":{"result":null,"error":{"code":"Not Found",…}}}`. A window
  with no session (a weekend): HTTP 200, `meta` and no `timestamp`. A window before the first
  trade: HTTP 400 `"Data doesn't exist for startDate = …"`. No `User-Agent` at all: still
  served on the day. A 429 is what Yahoo answers when called too fast (yfinance users report
  it since 2025).

BCB SGS (`research_notes/.../renda_fixa_macro_cambio.md`): since 2025-03-26 a window over 10
years is refused (HTTP 406 on daily series) and long requests can time out after about 30 s.

## Decisions made in this spec

Marked **(review)** where the spec picked a default a person should confirm.

| # | Question | Decision |
|---|---|---|
| 1 | Yahoo and the architecture | The owner approved amending ARCHITECTURE.md (2026-10-06): its "Do not use Yahoo Finance" becomes a Yahoo section, **for personal use only**, with the risks (undocumented, rate-limited, can break) and the mitigations (the ports, the nightly sync only, the screens reading only Postgres, another provider per asset). ADR-015 records Yahoo as the fifth `IPriceProvider`. ADR-010 (open registration) gets a note: Yahoo's terms are personal use, so this is revisited before anyone else uses the app. |
| 2 | Request | `GET {BaseUrl}{symbol}?period1=…&period2={now}&interval=1d&events=div%2Csplit&includeAdjustedClose=true`, `User-Agent` from `MarketData:Yahoo:UserAgent` (a desktop browser's), `Accept: application/json`, gzip. `period2` is always now, never `to`: a split Yahoo already applied to the closes must be among the events, or the raw close comes out wrong. `period1` is the day before `from` (London-midnight FX bars), or 1900-01-01 for a whole history. No cookie, no crumb: add them only when Yahoo demands them. |
| 3 | Pacing | Yahoo's requests start at least `MarketData:Yahoo:RequestInterval` (1 s) apart, across the process. **(review)** |
| 4 | Resilience | The existing per-provider pipeline (006 decision 4), and for Yahoo only, a 429 is retried with the same exponential backoff (2, 4, 8 s, jitter). Yahoo's `Retry-After` is not followed: it can be hours, and would hold the whole run. Still 429 after three retries: `ProviderRateLimitedException`, and the sync stops asking Yahoo for the rest of the run, as for any 429. The circuit breaker still ignores 429. The other providers keep 006's rule: a 429 is not retried. **(review)** |
| 5 | A bar's day | The date of its timestamp at `meta.gmtoffset`, read one hour later. Yahoo gives only today's offset, and a bar from the other half of the year was stamped at an offset one hour away: a London-midnight FX bar would land on the day before. Every session starts between local midnight and mid-morning, so an hour later never crosses into the next day, whichever half of the year the offset comes from. The exchange's time zone database is not used: the API runs with invariant globalization, which on Windows finds no IANA zone, and the same code must date bars alike in development and in the Linux container. Crypto's offset is 0, so crypto days are UTC days. A bar is kept only when on or before `to` **and** before the exchange's today (now at `gmtoffset`): an unfinished session is never stored. A null close is skipped. Only a day's first point is its bar: FX appends a quote stamped "now", which never stands in for a null close. |
| 6 | What `Close` means | **The raw traded price.** 007 records a split or a bonus as a `Split` movement (quantity added at price 0) and `SnapshotBuilder` values `Quantity × Close`, so a split-adjusted close would value the pre-split quantity at a fraction of its price. Yahoo's close is split-adjusted, so the adapter rebuilds it: raw = close × the product of numerator ÷ denominator over every split whose day is after the bar's day, rounded to `meta.priceHint` decimals. AAPL 2020-08-28: 124.8075 × 4 = 499.23. |
| 7 | Total return | `Price.AdjustedClose`, nullable, `numeric(18,8)`: Yahoo's `adjclose` as sent (split- and dividend-adjusted). Every other provider leaves it null. 026 reads `AdjustedClose ?? Close`. |
| 8 | Refusals | 404 → `ProviderSymbolUnknownException` → "O Yahoo Finance não encontrou o símbolo {símbolo}. Confira o símbolo do ativo no catálogo." 401 or 403 → `ProviderBlockedException` → "O Yahoo Finance recusou o acesso. Tente de novo mais tarde; se persistir, ele passou a exigir cookie e o adaptador precisa mudar." 400 "Data doesn't exist", or 200 without `timestamp` → an empty series. A body that is not the chart JSON (an HTML consent page) → "O provedor respondeu em um formato inesperado." (006). Both texts reach `/market-data` through `SyncErrorText`, as 019's do. |
| 9 | Wiring | `ProviderKind.Yahoo = 4`, stored in the existing `int` column. `YahooProvider` in `Infrastructure/MarketData/`, a typed client registered like the others, gzip on. The port gains two members with defaults, so the other four adapters do not change: `HistoryStart` (Yahoo: 1900-01-01; null: `BackfillYears` back) and `RevisesHistory` (Yahoo: true). |
| 10 | Full history | A Yahoo asset's first sync, and any whole-history load, asks from 1900-01-01: everything Yahoo has. The other providers keep `BackfillYears` (5). The BCB series reach back to `MarketData:Bcb:HistoryStart`, 1994-07-01: before the Plano Real the values are in older currencies. SGS is asked in windows of at most 5 years. |
| 11 | Loading older history once | **Assets:** `MarketAssets.HistoryLoadedAt` (`timestamptz`, null). Null means the next sync loads the asset's whole history and **replaces** its stored prices in one transaction, then stamps it. A new asset starts null; so does one whose provider or symbol is edited (decision 16). The migration stamps every existing row with its `LastSyncedAt`, so an asset already synced on brapi, Twelve Data, CoinGecko or Binance is not reloaded (CoinGecko would lose four years: it serves 365 days). **BCB series:** when the earliest stored day is more than 7 days after `HistoryStart`, the sync also asks for `[HistoryStart, earliest − 1]`, so a series synced with 5 years gets its older history once. All four configured series begin before 1994 (CDI and SELIC 1986, IPCA 1980, USD 1984); a series that begins later would be asked every night, so `HistoryStart` must not precede any configured series' first value. **Price benchmarks:** with no stored row, a whole-history load. **(review)** |
| 12 | Rebasing | Yahoo rebases `adjclose` at every dividend and split, and corrects data now and then. For a provider that `RevisesHistory`, the sync asks from 7 days before the latest stored day. If an overlapping day's `Close` or `AdjustedClose` differs from the stored one, it reloads the asset's whole history and upserts every row (a row Yahoo no longer sends is kept). "Differs": after rounding Yahoo's value to the column's 8 decimals, by more than a millionth of the stored value or 2 × 10⁻⁸, whichever is larger; a null against a number differs. Otherwise only the days not stored yet are written. A Yahoo price benchmark is checked the same way, on its stored value. |
| 13 | Snapshots after a price change | `MarketAssets.PricesRevisedFrom` (`date`, null) keeps the earliest day on or before the latest stored day whose stored `Close` a sync changed, filled in or (on a replace) removed. The rebuild after the sync (007) starts each holder's asset at the earlier of its nightly start and that day, through the existing `SnapshotRebuild` (ADR-011), and clears the marker once every holder's rebuild succeeded; a failed one leaves it for the next run. Durable, so a crash between the sync and the rebuild loses nothing. A rebase that moves only `AdjustedClose` sets nothing: snapshots read `Close`. **(review)** |
| 14 | Price benchmarks | They store `AdjustedClose ?? Close`: a benchmark measures total return, and a raw close would fall at a split. IVVB11 moves from brapi (which needs a token) to Yahoo `IVVB11.SA`; on Yahoo it has neither split nor dividend, so the values match its close. The migration deletes the stored IVVB11 rows, so the next sync reloads it whole from one source instead of mixing brapi's with Yahoo's. **(review)** |
| 15 | New classes | `MarketAssetClass.Index = 6` and `Currency = 7`, labelled "Índice" and "Câmbio": things compared, never held. `POST /api/investments/assets` refuses them (400) and `/investments` neither offers them nor lists them in its catalogue search. **(review)** |
| 16 | Editing a catalogue entry | `PATCH /api/market-data/assets/{id}` `{ provider, providerSymbol }`, with registration's rules. Ticker, class and currency do not change: movements are in the asset's currency (007). A change clears `HistoryLoadedAt`, so the next sync replaces the asset's prices with the new source's whole history (decision 11). Any signed-in user may edit, as any may register (ADR-010's shared catalogue). **(review)** |
| 17 | Yahoo symbols | Stored upper-cased, one row per series. Letters, digits and `. - ^ =` only. Where the suffix names the quote currency, the asset's currency must match: `.SA` → BRL, `-USD` → USD, `BRL=X` and `…BRL=X` → BRL, `…USD=X` → USD. **(review)** |
| 18 | The registration form | Yahoo first in the provider select, and the default: "Yahoo Finance (B3, EUA, índices, cripto em US$, câmbio — sem chave)". The symbol and the currency are suggested from ticker, class and provider until the person edits that field. For Yahoo: B3 classes `TICKER.SA` in BRL; US stock `TICKER` in USD; crypto `TICKER-USD` in USD; an index `^TICKER`, with `IBOV` → `^BVSP` (BRL), `SP500`/`SPX` → `^GSPC`, `NASDAQ` → `^IXIC`, `DOW` → `^DJI`; a currency `USD` → `BRL=X`, `XXX` → `XXXBRL=X` (BRL). brapi and Twelve Data suggest the ticker, Binance `TICKERBRL`, CoinGecko nothing. **(review)** |
| 19 | Benchmarks audit | Configured: CDI (SGS 12), SELIC (11), IPCA (433), USDBRL (1, PTAX) from BCB, keyless; IVVB11 from brapi, which needs a token. IVVB11 moves to Yahoo `IVVB11.SA` (decision 14). 008's `Returns:Benchmarks` does not change. No key-bound default remains. |
| 20 | Keys | None is needed. The brapi token, the Twelve Data key and the CoinGecko demo key become optional: they serve only assets someone keeps on those providers. `docs/market-data-keys.md` and `deploy/.env.example` say so. |
| 21 | E2E fakes | `FakeMarketDataProviders` answers Yahoo with a fake of its own (decision 9's members included): every calendar day from `to − 1000` days, a close of 12,50 on `to` and one cent lower for each day before it (7,50 from 500 days back), doubled before a 2:1 split on `to − 100`; dividends on `to − 30`, `to − 60` and `to − 90`, each taking 1 % off `AdjustedClose` for the days before it. The other kinds keep their closes. |

## Out of scope

- The comparison page (026)
- Market-level dividend or corporate-event tables: a new shared table needs an ADR amendment
- IFIX (Yahoo has none) and Tesouro Direto
- Moving an asset to another currency, or reconciling the movements of one
- A sync-time check of Yahoo's `meta.currency` against the catalogue's currency (decision 17
  catches the common cases at registration)
- Cookie and crumb handling (decision 2)

## Data model changes

Migration `AddYahooHistory`:

| Table | Column | Type | Notes |
|---|---|---|---|
| `Prices` | `AdjustedClose` | `numeric(18,8) NULL` | decision 7 |
| `MarketAssets` | `HistoryLoadedAt` | `timestamptz NULL` | decision 11; existing rows stamped with `LastSyncedAt` |
| `MarketAssets` | `PricesRevisedFrom` | `date NULL` | decision 13 |
| `Benchmarks` | — | — | IVVB11's rows deleted (decision 14) |

`ProviderKind.Yahoo = 4`, `MarketAssetClass.Index = 6` and `Currency = 7` live in the existing
`int` columns, which have no check constraint: no column change for them.

Generated SQL (`dotnet ef migrations script 20260924082437_AddAi AddYahooHistory`), to review
before it is applied anywhere:

```sql
START TRANSACTION;
ALTER TABLE "Prices" ADD "AdjustedClose" numeric(18,8);

ALTER TABLE "MarketAssets" ADD "HistoryLoadedAt" timestamp with time zone;

ALTER TABLE "MarketAssets" ADD "PricesRevisedFrom" date;

UPDATE "MarketAssets" SET "HistoryLoadedAt" = "LastSyncedAt";

DELETE FROM "Benchmarks" WHERE "Code" = 'IVVB11';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006065309_AddYahooHistory', '10.0.12');

COMMIT;
```

The three columns are nullable with no default, so adding them rewrites no table. The
`UPDATE` touches every catalogue row once; the `DELETE` only IVVB11's benchmark rows, which
the next sync reloads from Yahoo.

## API surface

```
PATCH /api/market-data/assets/{id}            new (decision 16)
      { provider, providerSymbol }
      200 the entry, historyLoadedAt null when the source changed
      400 provider | providerSymbol | currency  (registration's messages, decision 17's)
      404 unknown id
      409 "O símbolo '…' já está cadastrado no provedor …."

GET   /api/market-data/assets[/…]             each entry gains historyLoadedAt
GET   /api/market-data/assets/{id}/prices     each day gains adjustedClose (null off Yahoo)

POST  /api/market-data/assets                 provider "Yahoo"; class "Index" | "Currency"
      400 providerSymbol "Use um símbolo do Yahoo Finance, como PETR4.SA, AAPL, ^BVSP, BTC-USD ou BRL=X."
      400 currency       "O símbolo PETR4.SA é cotado em BRL no Yahoo Finance."

POST  /api/investments/assets                 (007)
      400 class | marketAssetId "Índices e câmbio servem para comparação e não entram na carteira."

GET   /api/market-data/sync-runs              summary.Yahoo, failures with decision 8's texts
```

## Configuration

```
MarketData:Bcb:HistoryStart         1994-07-01
MarketData:Yahoo:BaseUrl            https://query1.finance.yahoo.com/v8/finance/chart/
MarketData:Yahoo:UserAgent          a desktop Chrome's
MarketData:Yahoo:RequestInterval    00:00:01
MarketData:PriceBenchmarks:IVVB11   { Provider: Yahoo, Symbol: IVVB11.SA, Unit: Level }
```

## UI behaviour

`/market-data`, "Ativos":

- Registration: decision 18. The currency select follows the suggestion too.
- Each catalogue row has **Editar**. It opens a form "Editar fonte de {ticker}" with
  "Provedor" and "Símbolo no provedor" (suggested when the provider changes and the symbol was
  not typed), **Salvar** and **Cancelar**. A 400 shows under its field, a 409 as the sentence.
- A row whose `historyLoadedAt` is null says "Histórico completo na próxima sincronização."
- Classes "Índice" and "Câmbio" in the class select.
- The run list shows a "Yahoo Finance" line like any provider.

`/investments`, "Adicionar ativo": the class select has no "Índice" or "Câmbio", and the
catalogue search leaves those entries out.

Both themes, 1440 px and 390 px, nothing scrolling sideways.

## Test plan

Tests are written first. E2E covers every flow a person can see; unit and integration tests
cover only what E2E cannot reach.

### Unit — `api.tests/Unit`

**`YahooProvider`, against responses captured on 2026-10-06 (`api.tests/Fixtures/MarketData/yahoo-*.json`):**
1. AAPL around the 2020 split: bars dated in New York, `Close` raw (2020-08-28: 499.23), `AdjustedClose` Yahoo's, nothing through `double`
2. ITUB4 with both bonus issues: raw closes before 2025-03-18 are Yahoo's × 1.1 × 1.03, between the two × 1.03, after both as sent
3. The request: path, `period1` the day before `from` (1900-01-01 for a whole history), `period2` now, both event kinds, `includeAdjustedClose`, the `User-Agent`
4. BRL=X, read with a summer offset and with a winter one: bars stamped 23:00 UTC (London summer midnight) and 00:00 UTC (winter) both get their London day
5. Today's bar and anything after `to` are left out; null closes are skipped; FX's "now" point never stands in for a null close
6. A close is rounded to `priceHint` decimals; a missing `priceHint` leaves it as sent
7. 404 → `ProviderSymbolUnknownException`; 401 and 403 → `ProviderBlockedException`; 429 → `ProviderRateLimitedException`; 400 "Data doesn't exist" and a 200 without `timestamp` → empty; malformed bodies and an HTML page → `ProviderResponseInvalidException`
8. Two calls start at least `RequestInterval` apart (fake clock)
9. Through the container: Yahoo's 429 is retried and then succeeds; brapi's is still not retried

**BCB windowing:**
10. 1994-07-01 to 2026-10-05 is asked as consecutive windows of at most 5 years, none overlapping, in order, and the values come back as one series

**The rebasing check (`PriceRevision`):**
11. A difference beyond the tolerance, a null against a number, and float noise within it
12. The earliest change: a changed close, a filled gap, older history, a removed day on a replace; days after the latest stored one and an empty store change nothing

**Text and fakes:**
13. `SyncErrorText` gives decision 8's sentences
14. The Yahoo fake: a close of 12,50 on `to`, doubled before the split, `AdjustedClose` 1 % lower per dividend still ahead

### Integration — `api.tests/Integration`

15. **Migration:** on a database at `AddAi` with a synced brapi asset, a never-synced one, prices and IVVB11 rows, `AddYahooHistory` stamps `HistoryLoadedAt` with `LastSyncedAt` (null for the never-synced), keeps every price with a null `AdjustedClose`, and deletes only IVVB11's benchmark rows
16. A new Yahoo asset's first sync asks from 1900-01-01, writes `AdjustedClose`, and stamps `HistoryLoadedAt`
17. An asset whose source changed (null `HistoryLoadedAt`): its prices are replaced, not merged, and `PricesRevisedFrom` is the earliest day either source had
18. An overlap that matches: only the new days are written, no reload
19. An overlap whose `AdjustedClose` moved (a dividend): the whole history is reloaded and upserted, a stored day Yahoo no longer sends survives, and `PricesRevisedFrom` stays null
20. A corrected `Close` sets `PricesRevisedFrom`, and the rebuild after the sync rebuilds a holder's asset from that day and clears it; a failing rebuild keeps it
21. BCB: a series stored from 2021 is extended back to `HistoryStart` once; the next run asks only for the new days
22. A Yahoo price benchmark stores `AdjustedClose ?? Close`, from 1900-01-01 on its first sync

### E2E — `web/e2e/yahoo.spec.ts`, in its own project after `returns`

23. Register through Yahoo with the suggested symbols and currencies: an `StockBr` ticker gets `….SA` in BRL, a crypto `…-USD` in USD, `IBOV` as an index `^BVSP`, `USD` as a currency `BRL=X`; a manual sync on the fakes shows a "Yahoo Finance" line and no failure
24. A Yahoo symbol in the wrong currency is refused under "Moeda"
25. An asset held on brapi (100 at the fake 10,00) is edited to Yahoo on `/market-data`: the row says the history reloads at the next sync; after a sync the row shows Yahoo and the position is worth R$ 1.250,00
26. `/investments` offers neither "Índice" nor "Câmbio" and does not list an index in its catalogue search

Every existing test keeps passing.

## End-to-end verification

```
bash scripts/verify.sh
```

E2E on an isolated stack (API :5102, Vite :5202, database `financas_e2e_yahoo`), with
`MarketData__FakeProviders=true`.

Live, once, against `financas_live_yahoo` with `MarketData__FakeProviders=false`: register
ITUB4.SA, WEGE3.SA, BTC-USD, BRL=X, ^BVSP, ^GSPC and AAPL, sync, and record each first day and
count; compare AAPL 2020-08-28 and the ITUB4 days around 2025-03-18 raw against adjusted; check
that PTAX and CDI reach back to 1994-07-01.

## Definition of done

- `verify.sh` green; E2E green on the isolated stack
- ARCHITECTURE.md, ADR-015, ADR-010's note, `docs/market-data-keys.md` and `.env.example` match
  the code
- The migration's SQL is in this spec and was reviewed before being applied anywhere but a
  throwaway database
- The (review) decisions confirmed or changed by a person
