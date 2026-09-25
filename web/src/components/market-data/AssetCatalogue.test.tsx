import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { MarketAsset } from '@/api/finance';
import { renderWithClient, stubFetch, type SeenRequest } from '@/test-utils';
import AssetCatalogue from './AssetCatalogue';

const petr4: MarketAsset = {
  id: 'a-petr4',
  ticker: 'PETR4',
  name: 'Petrobras PN',
  class: 'StockBr',
  currency: 'BRL',
  provider: 'Brapi',
  providerSymbol: 'PETR4',
  isActive: true,
  lastSyncedAt: null,
  createdAt: '2026-09-24T12:00:00Z',
};

const btc: MarketAsset = { ...petr4, id: 'a-btc', ticker: 'BTC', name: 'Bitcoin', class: 'Crypto', currency: 'USD', provider: 'CoinGecko', providerSymbol: 'bitcoin' };

function stubApi(register: (request: SeenRequest) => { status?: number; body?: unknown }, catalogue: () => MarketAsset[] = () => [petr4]) {
  return stubFetch((request) => {
    const url = new URL(request.url, 'http://localhost');

    if (url.pathname !== '/api/market-data/assets') {
      return undefined;
    }

    return request.method === 'POST' ? register(request) : { body: catalogue() };
  });
}

const assetRequests = (seen: SeenRequest[]) =>
  seen.filter((request) => request.method === 'GET').map((request) => new URL(request.url, 'http://localhost'));

async function fillRegistration(values: { ticker: string; name?: string; class: string; provider: string; symbol: string; currency: string }) {
  await userEvent.type(screen.getByLabelText('Ticker'), values.ticker);
  if (values.name) {
    await userEvent.type(screen.getByLabelText('Nome (opcional)'), values.name);
  }
  await userEvent.selectOptions(screen.getByLabelText('Classe'), values.class);
  await userEvent.selectOptions(screen.getByLabelText('Provedor'), values.provider);
  await userEvent.type(screen.getByLabelText('Símbolo no provedor'), values.symbol);
  await userEvent.selectOptions(screen.getByLabelText('Moeda'), values.currency);
  await userEvent.click(screen.getByRole('button', { name: 'Cadastrar ativo' }));
}

describe('AssetCatalogue', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the catalogue and searches it by ticker or name', async () => {
    const seen = stubApi(() => ({ status: 500 }));
    renderWithClient(<AssetCatalogue />);

    const row = await screen.findByTestId('market-asset-a-petr4');
    expect(row).toHaveTextContent('PETR4');
    expect(row).toHaveTextContent('Petrobras PN');
    expect(row).toHaveTextContent('Ação (B3)');
    expect(row).toHaveTextContent('brapi');

    await userEvent.type(screen.getByLabelText('Buscar ativo'), 'petr');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(await screen.findByTestId('market-asset-a-petr4')).toBeInTheDocument();
    expect(assetRequests(seen).map((url) => url.searchParams.get('q'))).toEqual([null, 'petr']);
  });

  it('registers a ticker and shows it in the catalogue', async () => {
    let catalogue = [petr4];
    const seen = stubApi(
      () => {
        catalogue = [btc, petr4];
        return { status: 201, body: btc };
      },
      () => catalogue,
    );
    renderWithClient(<AssetCatalogue />);
    await screen.findByTestId('market-asset-a-petr4');

    await fillRegistration({ ticker: 'btc', name: 'Bitcoin', class: 'Crypto', provider: 'CoinGecko', symbol: 'bitcoin', currency: 'USD' });

    expect(await screen.findByTestId('market-asset-a-btc')).toHaveTextContent('Criptomoeda');
    expect(seen.find((request) => request.method === 'POST')?.body).toEqual({
      ticker: 'btc',
      name: 'Bitcoin',
      class: 'Crypto',
      provider: 'CoinGecko',
      providerSymbol: 'bitcoin',
      currency: 'USD',
    });
    expect(screen.getByLabelText('Ticker')).toHaveValue('');
  });

  /** Spec 019 web test 22. */
  it('offers Binance for crypto in reais, each provider with what it covers', async () => {
    const btcBrl: MarketAsset = { ...btc, id: 'a-btcbrl', currency: 'BRL', provider: 'Binance', providerSymbol: 'BTCBRL' };
    let catalogue = [petr4];
    const seen = stubApi(
      () => {
        catalogue = [btcBrl, petr4];
        return { status: 201, body: btcBrl };
      },
      () => catalogue,
    );
    renderWithClient(<AssetCatalogue />);
    await screen.findByTestId('market-asset-a-petr4');

    expect(within(screen.getByLabelText('Provedor')).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'brapi (B3: ações, FIIs, ETFs)',
      'CoinGecko (cripto)',
      'Twelve Data (ações dos EUA)',
      'Binance (cripto em reais)',
    ]);

    await fillRegistration({ ticker: 'BTC', class: 'Crypto', provider: 'Binance', symbol: 'btcbrl', currency: 'BRL' });

    const row = await screen.findByTestId('market-asset-a-btcbrl');
    expect(row).toHaveTextContent('Binance (BTCBRL)');
    expect(seen.find((request) => request.method === 'POST')?.body).toMatchObject({
      provider: 'Binance',
      providerSymbol: 'btcbrl',
      currency: 'BRL',
    });
  });

  it('shows a 400 under the field it names and a 409 verbatim', async () => {
    const currency = 'Ativos do CoinGecko são cotados em USD.';
    const duplicate = "O símbolo 'PETR4' já está cadastrado no provedor Brapi.";
    let answer: { status: number; body: unknown } = {
      status: 400,
      body: { title: 'One or more validation errors occurred.', errors: { currency: [currency] } },
    };
    stubApi(() => answer);
    renderWithClient(<AssetCatalogue />);
    await screen.findByTestId('market-asset-a-petr4');

    await fillRegistration({ ticker: 'BTC', class: 'Crypto', provider: 'CoinGecko', symbol: 'bitcoin', currency: 'BRL' });

    const form = screen.getByRole('form', { name: 'Cadastrar ativo' });
    expect(await within(form).findByText(currency)).toBeInTheDocument();
    expect(screen.queryByText('One or more validation errors occurred.')).not.toBeInTheDocument();

    answer = { status: 409, body: { title: 'Conflict', detail: duplicate } };
    await userEvent.click(screen.getByRole('button', { name: 'Cadastrar ativo' }));

    expect(await screen.findByText(duplicate)).toBeInTheDocument();
  });
});
