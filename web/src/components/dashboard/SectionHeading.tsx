/** The quiet label every dashboard card opens with: sentence case, muted ink. */
export default function SectionHeading({ id, children }: { id: string; children: React.ReactNode }) {
  return (
    <h3 id={id} className="text-muted-foreground font-sans text-sm font-semibold">
      {children}
    </h3>
  );
}
