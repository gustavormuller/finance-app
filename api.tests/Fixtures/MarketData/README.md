# Market-data provider fixtures

Response bodies the 006 provider tests (spec tests 1–9) feed to a fake
`HttpMessageHandler`. The tests never reach the network.

## Provenance: every file here is hand-written, none is captured

Spec 006 decision 9 asks for JSON "captured once from each real API". That was not
possible when these were written: on **2026-09-24** the development environment's egress
policy refused `api.bcb.gov.br`, `www3.bcb.gov.br`, `brapi.dev`, `api.coingecko.com` and
`api.twelvedata.com` (proxy `CONNECT` answered 403), and no provider keys were available.

Each file below was written by hand on 2026-09-24 from the provider's publicly documented
response shape. Dates and field names follow the documentation; the **values are
illustrative, not market data**. Replace each with a real capture (same file name, a
short range, keys removed from anything saved) and update this table.

| File | Provider · request | Status |
|---|---|---|
| `bcb-sgs-12-cdi.json` | BCB SGS · `bcdata.sgs.12/dados?formato=json&dataInicial=…&dataFinal=…` | hand-written from docs |
| `brapi-quote-petr4-historical.json` | brapi · `quote/PETR4?range=…&interval=1d` | hand-written from docs |
| `brapi-quote-unknown-ticker.json` | brapi · `quote/XXXX1`, HTTP 404 | hand-written from docs |
| `coingecko-bitcoin-market-chart.json` | CoinGecko · `coins/bitcoin/market_chart?vs_currency=usd&days=…&interval=daily` | hand-written from docs |
| `twelvedata-time-series-aapl.json` | Twelve Data · `time_series?symbol=AAPL&interval=1day&…` | hand-written from docs |

Rate-limit (429) and malformed bodies are inline strings in the tests, not files.

## Captured from the real API (019)

Recorded on **2026-09-25** with no key, from the live endpoint, unchanged except for
line breaks between candles. These are real responses.

| File | Provider · request | Status |
|---|---|---|
| `binance-klines-btcbrl.json` | Binance · `klines?symbol=BTCBRL&interval=1d&startTime=1789862400000&endTime=1790294399999&limit=1000` (20–24 Sep 2026) | captured, HTTP 200 |
| `binance-klines-invalid-symbol.json` | Binance · `klines?symbol=NOPEBRL&interval=1d&limit=3` | captured, HTTP 400 |
| `brapi-quote-missing-token.json` | brapi · `quote/BBAS3?range=5d&interval=1d`, no token | captured, HTTP 401 |
| `twelvedata-missing-apikey.json` | Twelve Data · `time_series?symbol=AAPL&interval=1day&…`, no key | captured, HTTP 401 |

## Captured from the real API (025)

Recorded on **2026-10-06** between 03:00 and 06:57 UTC, with no cookie and a desktop
browser's `User-Agent`, from `https://query1.finance.yahoo.com/v8/finance/chart/`, unchanged.
Every request ends `&interval=1d&events=div%2Csplit&includeAdjustedClose=true`. The tests
read them with the clock at the capture's `period2` (1791269793, 06:56:33 UTC).

| File | Request | Status |
|---|---|---|
| `yahoo-chart-aapl-2020-split.json` | `AAPL?period1=1598227200&period2=1599264000` (2020-08-24 to 09-05: the 4:1 split) | captured, HTTP 200 |
| `yahoo-chart-itub4-bonus-shares.json` | `ITUB4.SA?period1=1741737600&period2=1767398400` (2025-03-12 to 2026-01-03: both bonus issues) | captured, HTTP 200 |
| `yahoo-chart-brlx-dst.json` | `BRL%3DX?period1=1774396800&period2=1775174400` (2026-03-25 to 04-03: UK summer time begins) | captured, HTTP 200 |
| `yahoo-chart-brlx-today.json` | `BRL%3DX?period1=1790726400&period2=1791269793` (to now: today's null bar and the "now" point) | captured, HTTP 200 |
| `yahoo-chart-btcusd-today.json` | `BTC-USD?period1=1790812800&period2=1791269793` (to now: a null day and today's bar) | captured, HTTP 200 |
| `yahoo-chart-unknown-symbol.json` | `NOPE123.SA?period1=-2208988800&period2=…` | captured, HTTP 404 |
| `yahoo-chart-before-first-trade.json` | `ITUB4.SA?period1=946684800&period2=946771200` (before its first trade) | captured, HTTP 400 |
| `yahoo-chart-weekend.json` | `ITUB4.SA?period1=1790985600&period2=1791158399` (a Saturday and Sunday) | captured, HTTP 200 |

The windows of the first two end in the past, which the adapter never asks for (`period2`
is always now). Both are whole: no split of AAPL after 2020-08-31, and none of ITUB4 after
2025-12-26, so their closes are adjusted by exactly the splits they list.
