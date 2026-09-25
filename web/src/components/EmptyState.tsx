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
    <div className="text-muted-foreground glass rounded-2xl py-16 text-center">
      <p className="text-foreground text-sm font-medium">
        {filtered ? 'Nenhum lançamento corresponde a este filtro' : 'Nenhum lançamento ainda'}
      </p>

      <p className="mt-1 text-sm">
        {filtered
          ? 'Tente ampliar o período, ou limpar os filtros de conta e categoria.'
          : 'Registre o primeiro para começar a acompanhar para onde o dinheiro vai.'}
      </p>

      {children && <div className="mt-4">{children}</div>}
    </div>
  );
}
