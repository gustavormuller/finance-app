/**
 * The two different nothings a list can show.
 *
 * "You have not added anything yet" and "your filter matched nothing" look the same
 * on screen and mean opposite things — one asks for a first entry, the other asks you
 * to widen the range. Conflating them is the single most common way a list lies to
 * its reader, which is why the spec calls for them to be distinguishable.
 */
export default function EmptyState({
  filtered,
  children,
}: {
  filtered: boolean;
  children?: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="border-border text-muted-foreground border-t py-16 text-center">
      <p className="text-foreground text-sm font-medium">
        {filtered ? 'No transactions match this filter' : 'No transactions yet'}
      </p>

      <p className="mt-1 text-sm">
        {filtered
          ? 'Try widening the date range, or clearing the account and category filters.'
          : 'Add your first one to start tracking where the money goes.'}
      </p>

      {children && <div className="mt-4">{children}</div>}
    </div>
  );
}
