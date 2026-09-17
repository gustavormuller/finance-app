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
    return <p className="text-muted-foreground text-xs">Verificando…</p>;
  }

  // A transport failure — API down, proxy misconfigured — is itself a degraded
  // state. Rendering it beats a blank page or a thrown error boundary.
  const health: Health =
    isError || !data ? { status: 'degraded', database: 'unreachable' } : data;

  return (
    <dl className="text-muted-foreground flex items-center gap-4 text-xs">
      <div className="flex items-center gap-1.5">
        <Dot ok={health.status === 'ok'} />
        <dt>API</dt>
        {/*
          The value is printed exactly as the API reports it — 'ok', 'degraded',
          'unreachable'. It is a status word from the wire rather than a label, the
          e2e spec asserts on it, and translating it would make the page disagree
          with what the API actually said.
        */}
        <dd data-testid="health-status" className="font-medium">
          {health.status}
        </dd>
      </div>

      <div className="flex items-center gap-1.5">
        <Dot ok={health.database === 'ok'} />
        <dt>Banco</dt>
        <dd data-testid="health-database" className="font-medium">
          {health.database}
        </dd>
      </div>
    </dl>
  );
}

/** Shape as well as colour, so the state is not carried by hue alone. */
function Dot({ ok }: { ok: boolean }) {
  return (
    <span
      aria-hidden="true"
      className={
        ok
          ? 'size-1.5 rounded-full bg-green-600'
          : 'size-1.5 rounded-xs bg-amber-600'
      }
    />
  );
}
