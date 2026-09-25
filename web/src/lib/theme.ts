/**
 * The theme choice: Claro, Escuro, or whatever the system prefers. A per-device
 * convenience in `localStorage`, never a server setting; storage that cannot be read
 * or written — a private window, blocked site data — behaves as `system`.
 *
 * `index.html` applies the same rule before the first paint, so keep the two in step:
 * key `theme`, values `light` | `dark` | `system`, class `dark` on `<html>`.
 */
export type ThemeChoice = 'light' | 'dark' | 'system';

const KEY = 'theme';

export function readThemeChoice(): ThemeChoice {
  try {
    const stored = localStorage.getItem(KEY);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}

export function storeThemeChoice(choice: ThemeChoice): void {
  try {
    localStorage.setItem(KEY, choice);
  } catch {
    // Nothing to remember it in; the choice still applies until the page is left.
  }
}

/** The `prefers-color-scheme: dark` query, when the environment has one. */
export function darkQuery(): MediaQueryList | undefined {
  return typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : undefined;
}

export function applyTheme(choice: ThemeChoice): void {
  const dark = choice === 'dark' || (choice === 'system' && (darkQuery()?.matches ?? false));
  const root = document.documentElement;

  root.classList.toggle('dark', dark);
  // Native controls — scrollbars, date pickers, the select popup — follow this.
  root.style.colorScheme = dark ? 'dark' : 'light';
}
