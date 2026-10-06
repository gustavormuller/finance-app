import type { MarketAssetClass, ProviderKind } from '@/api/finance';

/** Classes traded on B3, which Yahoo spells `TICKER.SA`. */
const b3Classes: MarketAssetClass[] = ['StockBr', 'Fii', 'EtfBr', 'Bdr'];

/** Index names people type, as Yahoo spells them. */
const yahooIndices: Record<string, string> = {
  IBOV: '^BVSP',
  IBOVESPA: '^BVSP',
  SP500: '^GSPC',
  SPX: '^GSPC',
  NASDAQ: '^IXIC',
  DOW: '^DJI',
};

/**
 * The symbol a provider most likely knows a ticker by (spec 025, decision 18): a suggestion
 * the form fills in until the person types their own. An empty string where there is no
 * guess (CoinGecko's ids are names, not tickers).
 */
export function suggestSymbol(provider: ProviderKind, ticker: string, assetClass: MarketAssetClass): string {
  const symbol = ticker.trim().toUpperCase();
  if (symbol === '') {
    return '';
  }

  switch (provider) {
    case 'Yahoo':
      return yahooSymbol(symbol, assetClass);
    case 'Binance':
      return symbol.endsWith('BRL') ? symbol : `${symbol}BRL`;
    case 'CoinGecko':
      return '';
    default:
      return symbol;
  }
}

function yahooSymbol(ticker: string, assetClass: MarketAssetClass): string {
  if (b3Classes.includes(assetClass)) {
    return ticker.endsWith('.SA') ? ticker : `${ticker}.SA`;
  }

  switch (assetClass) {
    case 'Crypto':
      return ticker.includes('-') ? ticker : `${ticker}-USD`;
    case 'Index':
      return ticker.startsWith('^') ? ticker : (yahooIndices[ticker] ?? `^${ticker}`);
    case 'Currency':
      // Rates into reais: Yahoo's BRL=X is the dollar's.
      return ticker.endsWith('=X')
        ? ticker
        : ticker === 'USD' || ticker === 'USDBRL'
          ? 'BRL=X'
          : ticker.length === 3
            ? `${ticker}BRL=X`
            : `${ticker}=X`;
    default:
      return ticker;
  }
}

/**
 * The currency a provider quotes a symbol in, as far as it can be told: what the API checks
 * for Yahoo's suffixes (`.SA`, `-USD`, `…BRL=X`), each provider's own quote, then the class.
 */
export function suggestCurrency(provider: ProviderKind, symbol: string, assetClass: MarketAssetClass): 'BRL' | 'USD' {
  switch (provider) {
    case 'Brapi':
    case 'Binance':
      return 'BRL';
    case 'CoinGecko':
    case 'TwelveData':
      return 'USD';
    default: {
      const upper = symbol.trim().toUpperCase();
      if (upper.endsWith('.SA') || upper.endsWith('BRL=X') || upper.endsWith('-BRL') || upper === '^BVSP') {
        return 'BRL';
      }

      if (upper.endsWith('-USD') || upper.endsWith('USD=X')) {
        return 'USD';
      }

      return b3Classes.includes(assetClass) || assetClass === 'Currency' ? 'BRL' : 'USD';
    }
  }
}
