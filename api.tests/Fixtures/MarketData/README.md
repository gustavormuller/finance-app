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
