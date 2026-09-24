# Autonomous run 005–010 — status

One line per checkpoint. Baseline before the run (commit 118fc1e): .NET 363 tests passed,
web unit 43 passed, E2E 13 passed.

## Checkpoint plans

- 005 checkpoints: CP1 domain + data model + migration (tests 1–4, 19; Transfer kind, rule 3, suggester, OpeningBalance, seeding) · CP2 Dapper aggregations + endpoints (tests 5–18) · CP3 web dashboard + Transfer in UI (tests 20–23) · CP4 E2E (24–26) · handoff.
- 006 checkpoints: CP1 data model + AddMarketData migration + upsert store + ADR-003/`prices` amendments (tests 17, 18, database half of 19, declared-type half of 9) · CP2 ports, provider registry, options with SGS codes and units, four provider adapters against fixtures (tests 1–9) · CP3 MarketDataSync, per-provider resilience, BackgroundService + Cronos (tests 10–16, 23, 24) · CP4 market-data endpoints + manual-sync rate limit (tests 19–22) · CP5 web `/market-data` + E2E + handoff (tests 25, 26).
- 007 checkpoints: CP1 data model + AddInvestments migration (context halves of tests 16, 23, 24; declared-type half of 9) · CP2 domain `PositionCalculator`, movement rules, `SnapshotBuilder` (tests 1–15) · CP3 `RebuildSnapshots` in one transaction, `/api/investments` endpoints, `POST /rebuild`, summary, nightly rebuild after the sync under the gate (tests 16–27, HTTP halves of 16, 23, 24) · CP4 web `/investments`, asset detail, movement form, add asset (tests 28–30) · CP5 E2E + handoff (tests 31–33).

## Log

| Spec | Checkpoint | Commit | Result |
|---|---|---|---|
| 004 | handoff | bec084f | OK — 004 complete in code (tests 1–66 present); manual steps 1–8 pending human |
| 005 | CP1 domain + migration | f6878c6 | OK — .NET 372, web 43, E2E 13; 3 TransactionPersistenceTests flaked once (DEFERRED) |
| 005 | CP2 Dapper aggregations + endpoints | ff3722b | OK — .NET 397, web 43, E2E 13; CP1 flake root-caused (inotify, config reload disabled in test hosts) |
| 005 | CP3 web dashboard + Transfer/openingBalance in UI | 1ae3b22 | OK — .NET 397, web 67, E2E 13 |
| 005 | CP4 E2E + handoff | 7f4468d | OK — .NET 397, web 67, E2E 16; 005 complete in code, manual steps 1–5 pending human |
| 006 | CP1 data model + migration | 0ed7f0c | OK — .NET 410, web 67, E2E 16. Paused before CP2: spec's two ports vs ADR-015 needs a human decision |
| 006 | CP2 ports, registry, settings, four providers | 542089d | OK — .NET 486, web 67, E2E 16. ADR-015 amended; all fixtures hand-written (network blocked) and SGS codes unverified, both pending human (DEFERRED) |
| 006 | CP3 sync, per-provider resilience, nightly job | d86fa0b | OK — .NET 521, web 67, E2E 16. Sync tests 10–14 run against PostgreSQL, not as unit tests; job off in test hosts and E2E via `MarketData:ScheduledSync`; container TZ pending human (DEFERRED) |
| 006 | CP4 market-data endpoints, manual sync + rate limit | 3cc56e5 | OK — .NET 535, web 67, E2E 16. Limit counts the latest run of any trigger (DB); one process-wide gate shared with the nightly job; run after the 202 in the background |
| 006 | CP5 web `/market-data` + E2E | eb6847f | OK — .NET 539, web 74, E2E 18. E2E API runs on `MarketData:FakeProviders` (Development only, boot refused elsewhere), which also zeroes the manual-sync window so E2E reruns never hit the 429 |
| 006 | handoff | c71a180 | OK — 006 complete in code (tests 1–26 present); provider keys, live fixtures, SGS codes, container TZ and manual steps 1–7 pending human |
| 007 | CP1 data model + migration | 0044305 | OK — .NET 549, web 74, E2E 18. No ADR conflict; ARCHITECTURE.md's Investments data-model block is stale against the spec (not edited, pending human) |
| 007 | CP2 domain PositionCalculator, MovementRules, SnapshotBuilder | 2248ce2 | OK — .NET 580, web 74, E2E 18. Full precision in the calculator, rounded once at the column (avg 8 places, BRL 2, half to even); broker agreement to the cent pending human (manual step 2). Invented pt-BR copy for fees ≥ 0 pending review (DEFERRED) |
| 007 | CP3a rebuild in one transaction, asset and movement routes | 12ced45 | OK — .NET 603 (web and E2E not rerun at 3a; no web change). Movement write and its rebuild share one transaction; DELETE uncovering a later sell is a 409 (spec silent, pending human) |
| 007 | CP3b positions, summary, `POST /rebuild`, rebuild after the sync | 9b30c32 | OK — .NET 611, web 76, E2E 18. USD realised/dividends at each event's own FX, rebuild also after manual syncs and from before yesterday where rows are missing (all spec-silent, pending human, DEFERRED); invented pt-BR copy pending review |
