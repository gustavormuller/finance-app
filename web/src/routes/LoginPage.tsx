import { useSearch } from '@tanstack/react-router';

export default function LoginPage() {
  const { error } = useSearch({ from: '/login' });

  return (
    <section>
      <h2>Sign in</h2>

      {error === 'unverified' && (
        <p role="alert">
          Google reports that this e-mail address is not verified. Verify it with Google
          and try again — no account was created.
        </p>
      )}

      {/*
        A plain anchor, not a router link: /api/auth/google is served by the API, and
        the browser has to leave the application for Google and come back.
      */}
      <a href="/api/auth/google">Sign in with Google</a>
    </section>
  );
}
