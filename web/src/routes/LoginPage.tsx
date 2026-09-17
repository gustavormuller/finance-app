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
    // Same container as the shell's header, so "Entrar" lines up with the app name
    // rather than floating in the middle of the page under a left-aligned heading.
    <section className="mx-auto max-w-5xl px-4 py-16">
      <div className="max-w-md">
        <h2 className="text-2xl font-semibold tracking-tight">Entrar</h2>

        <p className="text-muted-foreground mt-2 text-sm">
          Use sua conta Google. Não há senha para criar nem para lembrar.
        </p>

        {message !== undefined && (
          <p
            role="alert"
            className="border-destructive/30 bg-destructive/5 text-destructive mt-6 rounded-md border p-3 text-sm"
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
          className="bg-primary text-primary-foreground hover:bg-primary/90 mt-6 inline-flex h-9 items-center justify-center rounded-md px-4 text-sm font-medium shadow-xs transition-colors"
        >
          Entrar com o Google
        </a>
      </div>
    </section>
  );
}
