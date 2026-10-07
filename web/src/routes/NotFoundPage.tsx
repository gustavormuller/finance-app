import { Link, useLocation } from '@tanstack/react-router';

import { Button } from '@/components/ui/button';

/**
 * An address that matches no page. It renders outside the sign-in guard, so it shows nothing
 * but the address typed; Início takes a visitor who is not signed in on to the sign-in page.
 */
export default function NotFoundPage() {
  const { pathname } = useLocation();

  return (
    <section className="px-4 py-10 sm:py-20">
      <div className="glass mx-auto max-w-md rounded-2xl p-6 sm:p-8">
        <h2 className="text-3xl font-semibold tracking-tight">Página não encontrada</h2>

        <p className="text-muted-foreground mt-2 text-sm">
          Nenhuma página do app fica em <code className="text-foreground break-words">{pathname}</code>.
        </p>

        <Button asChild size="lg" className="mt-6 w-full">
          <Link to="/">Voltar ao Início</Link>
        </Button>
      </div>
    </section>
  );
}
