import { useQuery } from '@tanstack/react-query';

type Health = {
  status: string;
  database: string;
};

async function fetchHealth(): Promise<Health> {
  // 503 is an expected answer here, not a transport failure: the body carries the
  // degraded state, so it is parsed rather than thrown on.
  const response = await fetch('/api/health');

  return (await response.json()) as Health;
}

export default function HealthRoute() {
  const { data, isPending, isError } = useQuery({
    queryKey: ['health'],
    queryFn: fetchHealth,
    retry: false,
  });

  if (isPending) {
    return <p>Checking…</p>;
  }

  // A transport failure — API down, proxy misconfigured — is itself a degraded
  // state. Rendering it beats a blank page or a thrown error boundary.
  const health: Health =
    isError || !data ? { status: 'degraded', database: 'unreachable' } : data;

  return (
    <dl>
      <dt>Status</dt>
      <dd data-testid="health-status">{health.status}</dd>
      <dt>Database</dt>
      <dd data-testid="health-database">{health.database}</dd>
    </dl>
  );
}
