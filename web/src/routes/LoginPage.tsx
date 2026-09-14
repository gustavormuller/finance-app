import { useSearch } from '@tanstack/react-router';

/**
 * Every ?error= the callback can send. An unknown code renders no message rather
 * than an empty box, so a stale link cannot produce a blank alert.
 */
const MESSAGES: Record<string, string> = {
  unverified:
    'Google reports that this e-mail address is not verified. Verify it with Google and try again — no account was created.',
  cancelled: 'Sign-in was cancelled. Nothing was created, and you can try again whenever you like.',
  auth_failed: 'Sign-in could not be completed. Try again, and if it keeps happening the problem is on our side.',
};

export default function LoginPage() {
  const { error } = useSearch({ from: '/login' });
  const message = error === undefined ? undefined : MESSAGES[error];

  return (
    <section>
      <h2>Sign in</h2>

      {message !== undefined && <p role="alert">{message}</p>}

      {/*
        A plain anchor, not a router link: /api/auth/google is served by the API, and
        the browser has to leave the application for Google and come back.
      */}
      <a href="/api/auth/google">Sign in with Google</a>
    </section>
  );
}
