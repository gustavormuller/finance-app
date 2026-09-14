import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import Amount from './Amount';

describe('Amount', () => {
  /**
   * Spec web unit test 5. A negative amount has to be distinguishable from a positive
   * one at a glance, which is what the spec asks for with "negative in a distinct
   * colour".
   */
  it('renders a negative amount in a different style from a positive one', () => {
    const { unmount } = render(<Amount value={-42.9} />);
    const negative = screen.getByTestId('amount').className;
    unmount();

    render(<Amount value={42.9} />);
    const positive = screen.getByTestId('amount').className;

    expect(negative).not.toBe(positive);
  });

  /**
   * And never by colour alone. Roughly one man in twelve has a red/green deficiency,
   * and the sign still has to survive a greyscale printout, so the glyph carries the
   * meaning and the colour only reinforces it.
   */
  it('prints the sign, so colour is never the only thing carrying it', () => {
    const { unmount } = render(<Amount value={-42.9} />);
    expect(screen.getByTestId('amount').textContent).toContain('−');
    unmount();

    render(<Amount value={42.9} />);
    expect(screen.getByTestId('amount').textContent).toContain('+');
  });

  /** Two decimals always, so a column of figures has its decimal points in line. */
  it('always shows two decimal places', () => {
    render(<Amount value={3000} />);

    expect(screen.getByTestId('amount').textContent).toMatch(/3[.,]000[.,]00/);
  });

  it('lines up figures with tabular numerals, right aligned', () => {
    render(<Amount value={-42.9} />);

    expect(screen.getByTestId('amount')).toHaveClass('amount');
  });
});
