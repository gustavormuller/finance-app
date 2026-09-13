---
name: new-feature
description: Session A — interview the user in depth about a feature, then write specs/NNN-name.md. Produces a spec, never implementation code.
disable-model-invocation: true
---

# Session A — Spec

Run this in plan mode, in a fresh session. It produces exactly one artifact:
`specs/NNN-<name>.md`. No implementation code.

Feature: `NNN-<name>` (ask the user if they did not say).

Read `docs/ARCHITECTURE.md` and the existing files in `specs/` so this spec is
consistent with what already exists.

Interview the user in detail using the AskUserQuestion tool. Ask about the data model,
edge cases, failure modes, UI behaviour, and tradeoffs. Skip obvious questions — dig
into the parts they have probably not thought through.

Keep interviewing until everything is covered, then write `specs/NNN-<name>.md`
containing:

- Goal, in two sentences
- Out of scope, explicitly
- Data model changes (tables, columns, indexes, migrations)
- API surface (method, path, request, response, status codes)
- UI behaviour, if any
- Test plan: unit tests, integration tests, and the E2E path if applicable
- An end-to-end verification step that proves the feature works
- Open questions, if any remain

Do not write any implementation code in this session.

## Checks before finishing

- Every entity the feature adds is user-owned and carries `UserId`, or the spec says
  explicitly why it is shared market data (`prices`, `benchmarks`).
- Any monetary value in the spec is `decimal` / `NUMERIC`, never a float.
- Nothing in the spec contradicts an ADR in `docs/ARCHITECTURE.md`. If it does, raise
  it with the user rather than writing the spec around it.
- The test plan describes tests that would actually fail against a broken
  implementation. If a listed test would pass on broken code, say so.
