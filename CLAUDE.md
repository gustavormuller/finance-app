# Personal finance app

Architecture, system design and ADR-001..017: `docs/ARCHITECTURE.md`
Read it before proposing any structural change. Every decision there is binding — if
a request contradicts an ADR, stop and say so instead of silently deviating.

## Commands
- Verify everything: `bash scripts/verify.sh`
- E2E: `bash scripts/verify-e2e.sh`
- API: `cd api && dotnet run`
- Web: `cd web && npm run dev`
- DB: `docker compose -f deploy/docker-compose.dev.yml up -d`
- Migration: `cd api && dotnet ef migrations add <Name>`

## Non-negotiable rules
- Everything in English: code, comments, identifiers, commit messages, docs.
- IMPORTANT: money is always `decimal`. Never `double` or `float`, not even in a local.
- Every domain entity has `UserId` and a global query filter. No exceptions.
  `prices` and `benchmarks` are shared market data — no `UserId`, no filter.
- `Domain/` must not reference EF Core or `HttpClient`.
- No Repository over EF Core. `Application/` uses `AppDbContext` directly.
- No MediatR, no domain events, no strict aggregate boundaries.
- Show the generated SQL of every migration before applying it.

## Workflow
- Work from a spec in `specs/`. If there is no spec for the task, stop and say so.
- Write the failing test before the implementation.
- Run `bash scripts/verify.sh` before claiming a task is done. Show the output.
- Keep diffs under ~200 lines. If a task needs more, split it and tell me.
