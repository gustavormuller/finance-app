/**
 * The two different nothings a list can show.
 *
 * "You have not added anything yet" and "your filter matched nothing" look the same
 * on screen and mean opposite things — one asks for a first entry, the other asks you
 * to widen the range. Conflating them is the single most common way a list lies to
 * its reader, which is why the spec calls for them to be distinguishable.
 */
export default function EmptyState(props: {
  filtered: boolean;
  children?: React.ReactNode;
}): React.JSX.Element {
  throw new Error(`EmptyState is not implemented (filtered=${props.filtered})`);
}
