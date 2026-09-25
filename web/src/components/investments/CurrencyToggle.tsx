import { useCurrencyChoice, type Currency } from '@/lib/currency';
import { cn } from '@/lib/utils';

const OPTIONS: [Currency, string, string][] = [
  ['BRL', 'R$', 'Valores em reais'],
  ['USD', 'US$', 'Valores em dólar, à cotação mais recente'],
];

/**
 * R$ | US$ (016, decision 10): a segmented control like the theme's, in the header of
 * every investments page. The choice is the device's, and every page follows it.
 */
export default function CurrencyToggle({ className }: { className?: string }): React.JSX.Element {
  const [choice, choose] = useCurrencyChoice();

  return (
    <div role="group" aria-label="Moeda" className={cn('bg-secondary flex items-center gap-0.5 rounded-xl p-1', className)}>
      {OPTIONS.map(([value, label, title]) => (
        <button
          key={value}
          type="button"
          aria-pressed={choice === value}
          title={title}
          onClick={() => choose(value)}
          className={cn(
            'text-muted-foreground hover:text-foreground focus-visible:ring-ring/50 h-8 rounded-lg px-3 text-sm font-semibold transition-colors outline-none focus-visible:ring-[3px]',
            choice === value && 'bg-card text-foreground shadow-sm',
          )}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
