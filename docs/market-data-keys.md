# Market-data keys

Which provider keys the market-data sync can use, what each one unlocks, and how to set
them. Everything here was checked against the live APIs and the providers' own pages on
**2026-09-25**. Free-tier limits change; the linked pages are the source of truth.

The app runs without any key. Keys only widen what it can price.

## At a glance

| Provider | Key needed? | Without a key | With the key | Setting |
|---|---|---|---|---|
| BCB SGS | no | CDI, SELIC, IPCA, USDBRL | — | — |
| Binance | no | crypto pairs quoted in BRL (`BTCBRL`, `ETHBRL`, `SOLBRL`, `USDTBRL`, …), 5 years of history | — | — |
| CoinGecko | optional | crypto in USD, the last 365 days | the same 365 days, with a quota of your own | `MarketData:CoinGecko:DemoKey` |
| brapi | for most tickers | `PETR4`, `VALE3`, `MGLU3`, `ITUB4` only | every B3 ticker, and the IVVB11 benchmark | `MarketData:Brapi:Token` |
| Twelve Data | yes | nothing | US stocks | `MarketData:TwelveData:Key` |

When a provider refuses because its key is empty, the sync run on `/market-data` says
so under the ticker, e.g. "O brapi exige um token para BBAS3. Configure
MarketData:Brapi:Token." That message means the setting below is empty or did not reach
the API. "O provedor recusou a chave de acesso." means a key is set but the provider
rejected it, either because the key is wrong or because the plan does not cover the
request.

## brapi token

**What it unlocks.** Without a token brapi answers four tickers: PETR4, VALE3, MGLU3 and
ITUB4. Everything else gets HTTP 401 `MISSING_TOKEN`. That covers any other B3 stock,
FII, ETF or BDR, unknown tickers too, and **IVVB11**, the S&P 500 benchmark that
`MarketData:PriceBenchmarks` reads through brapi. Until a token is set, every run shows
IVVB11 as failed, and the S&P 500 comparison on the returns pages reads "Sem dados".

**Sign up.** <https://brapi.dev/dashboard> (it redirects to the login and sign-up page).
Plans: <https://brapi.dev/pricing>. Docs: <https://brapi.dev/docs>.

**Free tier ("Gratuito").** 15,000 requests a month, history **up to 3 months**, one
request at a time. The paid plans as listed on the day: Startup, R$ 99,99/month on annual
billing, 150,000 requests and up to 1 year of history. Pro, R$ 116,66/month on annual
billing, 500,000 requests and more than 10 years.

> **Caveat: history on the free plan.** The first sync of an asset asks brapi for
> `MarketData:BackfillYears` (5) years. brapi's docs say a request beyond the plan is
> answered with HTTP 403, and the run would then show "O provedor recusou a chave de
> acesso." for that ticker. This could not be tried without a token. If it happens with
> a free token, the options are a plan that covers the range, or a code change so the
> adapter asks for no more than the plan serves (spec 019, out of scope).

## Twelve Data key

**What it unlocks.** US stocks (provider "Twelve Data" at registration, currency USD).
Without a key every request is HTTP 401, so no US stock syncs.

**Sign up.** <https://twelvedata.com/register>. The key is under
<https://twelvedata.com/account/api-keys> once logged in. Plans:
<https://twelvedata.com/pricing>.

**Free tier ("Basic").** 800 API credits a day, 8 a minute. One sync of one symbol is one
request. A handful of US stocks a night is far inside it.

## CoinGecko demo key (optional)

**What it unlocks.** CoinGecko already works without a key: the public API serves crypto
priced in USD for the last 365 days, and the app never asks for more
(`MarketData:CoinGecko:MaxHistoryDays` 365). The demo key gives the app a monthly quota
and a rate limit of its own, instead of the shared keyless one. It does **not** give more
history: the demo plan also serves one year of daily data. For crypto in reais with five
years of history, register the asset under Binance instead.

**Sign up.** Choose the free Demo plan at <https://www.coingecko.com/en/api/pricing> (no
credit card), then create the key in the Developer Dashboard. The steps are in
<https://docs.coingecko.com/docs/setting-up-your-api-key>.

**Free tier ("Demo").** 10,000 call credits a month; the pricing page listed 100 calls
a minute.

## Binance and BCB: nothing to set

- **Binance** public market data (`api.binance.com`) needs no account and no key. A
  five-year backfill of one pair is two requests. Binance refuses some countries, the
  United States among them, with HTTP 451. The server must run in a country Binance
  serves; it answered from Brazil on 2026-09-25. From a refused region, the run shows
  "Falha de comunicação com o provedor." for every Binance asset.
- **BCB SGS** is open. Series 12 (CDI), 11 (SELIC), 433 (IPCA) and 1 (USD) were checked
  live on 2026-09-25 and match `appsettings.json`.

## Setting a key

The configuration keys are `MarketData:Brapi:Token`, `MarketData:TwelveData:Key` and
`MarketData:CoinGecko:DemoKey` (`MarketDataOptions` in
`api/Application/MarketData/MarketDataOptions.cs`). Set only the ones you have. Never
commit a key: `appsettings.json` keeps them empty, and a test checks that.

### Development

User secrets (the API project's `UserSecretsId` is `finance-app-api`). They are read only
when the environment is Development, which `dotnet run` uses by default.

```bash
cd api
dotnet user-secrets set "MarketData:Brapi:Token" "<token>"
dotnet user-secrets set "MarketData:TwelveData:Key" "<key>"
dotnet user-secrets set "MarketData:CoinGecko:DemoKey" "<key>"
dotnet user-secrets list          # shows what is set
```

Restart the API afterwards; it reads configuration at startup.

### Production

In `/opt/finance/.env` on the server (`deploy/.env.example` lists them). `__` stands for
`:` in an environment variable:

```env
MarketData__Brapi__Token=<token>
MarketData__TwelveData__Key=<key>
MarketData__CoinGecko__DemoKey=<key>
```

Delete the line, or leave the value empty, for a key you do not have. A leftover
placeholder such as `<brapi-token>` is sent as a real key and is refused ("O provedor
recusou a chave de acesso.").

The API container reads the file when it is created (`env_file` in
`deploy/docker-compose.yml`), so recreate it:

```bash
cd /opt/finance/src
docker compose --env-file /opt/finance/.env -f deploy/docker-compose.yml up -d --force-recreate api
```

A full `deploy/deploy.sh` run recreates it too. Keep the file `chmod 600`; `deploy.sh`
refuses to run otherwise.

## Confirming it worked

1. Open `/market-data` (it is not in the menu; type the address).
2. Make sure the catalogue has an asset that needs the key, e.g. `BBAS3` (brapi) or
   `AAPL` (Twelve Data). Register one with "Cadastrar ativo" if not.
3. Click **Sincronizar agora**. A manual sync can run once every 10 minutes. The new row
   says "Em andamento" until it finishes.
4. In that row, the provider's line shows rows written and no "exige um token" or
   "exige uma chave" failure. For brapi, IVVB11 no longer fails either.

If the line still says "exige …", the API did not receive the setting. Check
`dotnet user-secrets list` in development. In production, check the `.env` line name
(two underscores) and that the container was recreated.
