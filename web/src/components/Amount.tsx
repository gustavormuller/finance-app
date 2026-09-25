import { cn } from '@/lib/utils';

/**
 * Three decisions, made once: tabular figures so a column of numbers lines up, right
 * alignment so the decimal points do too, and colour used for exactly one job —
 * telling an arrival from a departure.
 *
 * Colour is never the only carrier of the sign. The sign itself is always printed, so
 * the row still reads correctly in greyscale and for a red/green deficiency.
 */
export default function Amount({
  value,
  className,
}: {
  value: number;
  className?: string;
}): React.JSX.Element {
  const magnitude = Math.abs(value).toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

  return (
    <span
      data-testid="amount"
      className={cn(
        'amount tabular-nums',
        value < 0 ? 'text-foreground' : 'text-positive',
        className,
      )}
    >
      {/* U+2212, a real minus sign: it is the width of a digit, so a column of
          negatives stays aligned where a hyphen would not. */}
      {value < 0 ? '−' : '+'}
      {magnitude}
    </span>
  );
}
