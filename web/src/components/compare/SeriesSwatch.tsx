import { lineColour, lineDash } from './series';

/** A short stroke in the series' colour and dash, beside its name. */
export default function SeriesSwatch({ index }: { index: number }) {
  return (
    <svg aria-hidden="true" width="18" height="8" viewBox="0 0 18 8" className="shrink-0">
      <line x1="1" y1="4" x2="17" y2="4" stroke={lineColour(index)} strokeWidth="2.5" strokeDasharray={lineDash(index)} strokeLinecap="round" />
    </svg>
  );
}
