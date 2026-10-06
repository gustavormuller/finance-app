/**
 * What every Recharts chart shares: the tooltip drawn as a solid popover in the
 * theme's colours, and money formatting for axes and tooltips. Colours are CSS
 * variables, so a chart follows the theme without re-rendering.
 */
export const tooltipProps = {
  separator: ': ',
  itemStyle: { color: 'var(--foreground)' },
  labelStyle: { color: 'var(--muted-foreground)', marginBottom: 4 },
  contentStyle: {
    background: 'var(--popover)',
    border: '1px solid var(--border)',
    borderRadius: 12,
    boxShadow: '0 8px 24px rgb(0 0 0 / 0.18)',
    fontSize: 12,
  },
} as const;

export const axisTick = { fill: 'var(--muted-foreground)', fontSize: 11 } as const;

export const money = (value: number) =>
  value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Axis ticks only: whole reais with pt-BR grouping, so the axis stays narrow. */
export const wholeMoney = (value: number) => value.toLocaleString('pt-BR', { maximumFractionDigits: 0 });

/** The category ring's colours, in rank order; past five the rest share the muted ink. */
const seriesColours = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)', 'var(--chart-5)'];

export const seriesColour = (index: number) => seriesColours[index] ?? 'var(--muted-foreground)';
