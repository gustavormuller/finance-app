/**
 * A signed amount of money.
 *
 * The three decisions variant C left implicit, made explicit and made once: tabular
 * figures so a column of numbers lines up, right alignment so the decimal points do
 * too, and colour used for exactly one job — telling an arrival from a departure.
 *
 * Colour is never the only carrier of the sign. The sign itself is always printed, so
 * the row still reads correctly in greyscale and for a red/green deficiency.
 */
export default function Amount(props: {
  value: number;
  currency?: string;
  className?: string;
}): React.JSX.Element {
  throw new Error(`Amount is not implemented (value=${props.value})`);
}
