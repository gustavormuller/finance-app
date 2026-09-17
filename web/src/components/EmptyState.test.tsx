import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import EmptyState from './EmptyState';

describe('EmptyState', () => {
  /**
   * Spec web unit test 4. These two states look identical — an empty list — and mean
   * opposite things. One asks for a first transaction, the other asks you to widen
   * the date range. The wording has to separate them.
   */
  it('says something different for no data than for no results', () => {
    const { unmount } = render(<EmptyState filtered={false} />);
    const noData = document.body.textContent ?? '';
    unmount();

    render(<EmptyState filtered />);
    const noResults = document.body.textContent ?? '';

    expect(noData).not.toBe('');
    expect(noResults).not.toBe('');
    expect(noData).not.toBe(noResults);
  });

  it('invites a first entry when there is no data at all', () => {
    render(<EmptyState filtered={false} />);

    expect(screen.getByText(/nenhum lançamento ainda/i)).toBeInTheDocument();
  });

  it('points at the filter when the filter is what emptied the list', () => {
    render(<EmptyState filtered />);

    expect(screen.getByText(/nenhum lançamento corresponde/i)).toBeInTheDocument();
  });
});
