import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Category } from '@/api/finance';
import { categoryKindLabels } from '@/lib/labels';
import { renderWithClient, stubFetch } from '@/test-utils';

import CategoriesPage from './CategoriesPage';

const transfer: Category = {
  id: 'cat-transfer',
  name: 'Transferência',
  kind: 'Transfer',
  parentId: null,
  createdAt: '2026-09-01T00:00:00Z',
};

describe('CategoriesPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** 005 amendment 1: the wire member is English, the screen reads Portuguese. */
  it('labels the Transfer kind in Portuguese', () => {
    expect(categoryKindLabels.Transfer).toBe('Transferência');
  });

  /** 005 amendment 1: existing users create Transferência by hand, so the form must offer it. */
  it('creates a Transfer category', async () => {
    const user = userEvent.setup();
    const seen = stubFetch((request) =>
      request.method === 'POST' ? { status: 201, body: transfer } : { body: [] },
    );

    renderWithClient(<CategoriesPage />);

    await user.click(screen.getByRole('button', { name: 'Nova categoria' }));
    await user.type(screen.getByLabelText('Nome'), 'Transferência');
    await user.selectOptions(screen.getByLabelText('Tipo'), 'Transferência');
    await user.click(screen.getByRole('button', { name: 'Criar categoria' }));

    await waitFor(() =>
      expect(seen.find((request) => request.method === 'POST')?.body).toEqual({
        name: 'Transferência',
        kind: 'Transfer',
        parentId: null,
      }),
    );
  });

  /** A seeded Transferência must be listed, not silently hidden by a two-kind page. */
  it('lists Transfer categories under their own heading', async () => {
    stubFetch(() => ({ body: [transfer] }));

    renderWithClient(<CategoriesPage />);

    expect(await screen.findByText('Transferência')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Transferências' })).toBeInTheDocument();
  });
});
