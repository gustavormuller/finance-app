import { Monitor, Moon, Sun } from 'lucide-react';
import { useEffect, useState } from 'react';

import { applyTheme, darkQuery, readThemeChoice, storeThemeChoice, type ThemeChoice } from '@/lib/theme';
import { cn } from '@/lib/utils';

const OPTIONS: [ThemeChoice, string, typeof Sun][] = [
  ['light', 'Claro', Sun],
  ['dark', 'Escuro', Moon],
  ['system', 'Sistema', Monitor],
];

/**
 * Claro, Escuro or Sistema (012, decision 3). Three pressed-state buttons rather than a
 * menu: the choice is always visible, and each is one click. Under Sistema the page
 * follows the operating system live, not only at load.
 */
export default function ThemeToggle({ className }: { className?: string }): React.JSX.Element {
  const [choice, setChoice] = useState<ThemeChoice>(readThemeChoice);

  useEffect(() => {
    applyTheme(choice);

    if (choice !== 'system') {
      return;
    }

    const query = darkQuery();
    const follow = () => applyTheme('system');
    query?.addEventListener('change', follow);

    return () => query?.removeEventListener('change', follow);
  }, [choice]);

  return (
    <div role="group" aria-label="Tema" className={cn('bg-secondary flex items-center gap-0.5 rounded-full p-1', className)}>
      {OPTIONS.map(([value, label, Icon]) => (
        <button
          key={value}
          type="button"
          aria-label={label}
          aria-pressed={choice === value}
          title={label}
          onClick={() => {
            storeThemeChoice(value);
            setChoice(value);
          }}
          className={cn(
            'text-muted-foreground hover:text-foreground focus-visible:ring-ring/50 flex size-8 items-center justify-center rounded-full transition-colors outline-none focus-visible:ring-[3px]',
            choice === value && 'bg-card text-foreground shadow-sm',
          )}
        >
          <Icon className="size-4" aria-hidden="true" />
        </button>
      ))}
    </div>
  );
}
