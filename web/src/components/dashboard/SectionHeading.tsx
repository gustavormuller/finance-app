/** The small uppercase label every dashboard section opens with, as the categories page uses. */
export default function SectionHeading({ id, children }: { id: string; children: React.ReactNode }) {
  return (
    <h3 id={id} className="text-muted-foreground text-xs font-semibold tracking-[0.1em] uppercase">
      {children}
    </h3>
  );
}
