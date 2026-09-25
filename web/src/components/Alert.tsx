/**
 * A refusal the user has to read.
 *
 * Every 409 and every failed write ends up here, which is the whole reason the
 * endpoints compose a sentence instead of returning a bare status: "'Nubank' ainda
 * tem 6 lançamento(s)" tells you what to do next, and a red line of unboxed text
 * beside a heading does not read as something addressed to you.
 */
export default function Alert({ children }: { children: React.ReactNode }) {
  return (
    <p
      role="alert"
      className="border-destructive/30 bg-destructive/5 text-destructive mb-6 rounded-xl border p-3 text-sm"
    >
      {children}
    </p>
  );
}
