# 017 — Cleanup: comments that earn their place, and no dead code

## Goal

Make the code quicker to read by deleting what a reader has to skip. That means comments that
repeat the code, notes about how the work was organised (checkpoints, deferred-item pointers),
and code nothing calls. Behaviour does not change.

## Out of scope

- Any change in behaviour, API shape, database schema, UI copy or test assertions.
- Renames, moving code between files, and refactors. Those belong to 020.
- Formatting churn. There is no formatter configured, and this spec does not add one.
- `api/Migrations/` (generated), `docs/` and `specs/`.

## Decisions made in this spec

| # | Question | Decision |
|---|---|---|
| 1 | What goes | A comment that says what the next line plainly does. An XML doc or JSDoc whose text is the member's name in other words. Commented-out code. Pointers to the process: checkpoint ids (`CP1`, `checkpoint 3`), `DEFERRED, 008 CP1`, `pending human`, `STATUS`, `handoff`. A spec-number prefix (`013:`, `(016)`) on a comment that stands without it. |
| 2 | What stays | Why a thing is done a non-obvious way. An invariant, a unit, a rounding or time-zone rule. A security or tenancy note. A workaround with its cause. A reference that sends the reader to a spec **decision** for a rule's full rationale (`see specs/008-returns.md, decision 3`). Anything the code cannot say itself. |
| 3 | Rewording | When a comment mixes rationale with process notes, the process part goes and the rationale stays, shortened if it can be. No new comments except to replace a longer one. |
| 4 | Tests | Test names already say what is tested, so doc comments that repeat the name go. `Spec NNN test N` references stay: they are how a spec's test plan is traced to its tests. |
| 5 | Dead code | Private members, exports, files and parameters that nothing uses are removed. They are found with the compiler, temporary analyser settings (not committed) and an ad-hoc `knip` run on the web. Public API members that only tests use stay. |
| 6 | Size | Split by area, with one commit per folder or group of folders. The diff is mostly deletions, so commits can pass ~200 lines when they only delete. |

## Test plan

No new tests. The existing suites prove behaviour did not change:

```
bash scripts/verify.sh
E2E, isolated (never scripts/verify-e2e.sh: it stops the shared database)
```

The PR states the comment-line count before and after, per area.

## Definition of done

- `verify.sh` green, E2E green, no test assertion changed.
- The diff has no change a reader would call a behaviour change.
