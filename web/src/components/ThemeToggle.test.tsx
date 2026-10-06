import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import ThemeToggle from './ThemeToggle';

/** A `matchMedia` whose `prefers-color-scheme: dark` answer is `dark`. */
function prefers(dark: boolean) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: dark && query.includes('dark'),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );
}

describe('ThemeToggle (012)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    document.head.querySelectorAll('meta[name="theme-color"]').forEach((meta) => meta.remove());
  });

  /** Spec 012 test 1. */
  it('offers Claro, Escuro and Sistema, and Escuro darkens the page and is remembered', async () => {
    prefers(false);
    const user = userEvent.setup();
    render(<ThemeToggle />);

    expect(screen.getByRole('button', { name: 'Claro' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sistema' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Escuro' }));

    expect(document.documentElement).toHaveClass('dark');
    expect(localStorage.getItem('theme')).toBe('dark');
    expect(screen.getByRole('button', { name: 'Escuro' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Claro' }));

    expect(document.documentElement).not.toHaveClass('dark');
    expect(localStorage.getItem('theme')).toBe('light');
  });

  /** Spec 012 test 2. */
  it('follows the system when Sistema is chosen', async () => {
    prefers(true);
    localStorage.setItem('theme', 'light');
    const user = userEvent.setup();
    render(<ThemeToggle />);

    expect(document.documentElement).not.toHaveClass('dark');

    await user.click(screen.getByRole('button', { name: 'Sistema' }));

    expect(document.documentElement).toHaveClass('dark');
    expect(localStorage.getItem('theme')).toBe('system');
  });

  /** Spec 022 test 5: the toolbar follows the theme applied, not only the system's. */
  it("sets both theme-color metas to the applied theme's ground", async () => {
    prefers(false);
    document.head.insertAdjacentHTML(
      'beforeend',
      '<meta name="theme-color" content="#f1f2f7" media="(prefers-color-scheme: light)">' +
        '<meta name="theme-color" content="#0b0c12" media="(prefers-color-scheme: dark)">',
    );
    const themeColors = () =>
      [...document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')].map((meta) => meta.content);
    const user = userEvent.setup();
    render(<ThemeToggle />);

    await user.click(screen.getByRole('button', { name: 'Escuro' }));
    expect(themeColors()).toEqual(['#0b0c12', '#0b0c12']);

    await user.click(screen.getByRole('button', { name: 'Claro' }));
    expect(themeColors()).toEqual(['#f1f2f7', '#f1f2f7']);

    prefers(true);
    await user.click(screen.getByRole('button', { name: 'Sistema' }));
    expect(themeColors()).toEqual(['#0b0c12', '#0b0c12']);
  });

  /** Spec 012 test 3. */
  it('treats storage it cannot read as Sistema', () => {
    prefers(true);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    render(<ThemeToggle />);

    expect(screen.getByRole('button', { name: 'Sistema' })).toHaveAttribute('aria-pressed', 'true');
    expect(document.documentElement).toHaveClass('dark');
  });
});
