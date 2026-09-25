import { useSearch } from '@tanstack/react-router';

/**
 * Every ?error= the callback can send. An unknown code renders no message rather
 * than an empty box, so a stale link cannot produce a blank alert.
 */
const MESSAGES: Record<string, string> = {
  unverified:
    'O Google informa que este e-mail não está verificado. Verifique-o com o Google e tente de novo — nenhuma conta foi criada.',
  cancelled: 'A entrada foi cancelada. Nada foi criado, e você pode tentar de novo quando quiser.',
  auth_failed:
    'Não foi possível concluir a entrada. Tente de novo; se continuar acontecendo, o problema é do nosso lado.',
};

export default function LoginPage() {
  const { error } = useSearch({ from: '/login' });
  const message = error === undefined ? undefined : MESSAGES[error];

  return (
    // 012: a single glass card in the middle of the page; the product name stays in
    // the shell's top bar above it.
    <section className="px-4 py-10 sm:py-20">
      <div className="glass mx-auto max-w-md rounded-2xl p-6 sm:p-8">
        <h2 className="text-3xl font-semibold tracking-tight">Entrar</h2>

        <p className="text-muted-foreground mt-2 text-sm">
          Use sua conta Google. Não há senha para criar nem para lembrar.
        </p>

        {message !== undefined && (
          <p
            role="alert"
            className="border-destructive/30 bg-destructive/5 text-destructive mt-6 rounded-xl border p-3 text-sm"
          >
            {message}
          </p>
        )}

        {/*
          A plain anchor, not a router link: /api/auth/google is served by the API, and
          the browser has to leave the application for Google and come back.
        */}
        <a
          href="/api/auth/google"
          className="bg-primary text-primary-foreground hover:bg-primary/90 mt-6 inline-flex h-11 w-full items-center justify-center rounded-xl px-4 text-sm font-semibold shadow-sm transition-colors"
        >
          Entrar com o Google
        </a>
      </div>
    </section>
  );
}
