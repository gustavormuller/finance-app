---
name: review-feature
description: Session C — review an implemented feature's diff against its spec in a fresh session, report only real gaps, then fix them.
disable-model-invocation: true
---

# Session C — Review

Run this in a fresh session, after Session B has shipped. A reviewer that did not
write the code catches more.

Use a subagent to review the diff for feature `NNN` against `specs/NNN-<name>.md`.

Check that:

- every requirement in the spec is implemented
- every listed edge case has a test
- the tests would actually fail if the implementation were wrong
- no ADR in `docs/ARCHITECTURE.md` was violated
- nothing outside the feature's scope was changed

Report only gaps that affect correctness or stated requirements. Do not report style
preferences. Do not suggest additional abstraction.

## Specific things worth checking in this repository

- Any new user-owned entity has `UserId` **and** a global query filter, plus the
  mandatory two-user isolation test (write as user A, assert user B sees none of it).
- `prices` and `benchmarks` are shared market data: they must have neither.
- Every monetary value is `decimal` / `NUMERIC`, including locals and DTOs.
- `Domain/` references neither EF Core nor `HttpClient`.
- No Repository wrapper appeared over `AppDbContext`.
- Any new port has more than one real or foreseen implementation (ADR-015).

## After the review

Fix the real findings, then re-run `bash scripts/verify.sh` and show the output.

Ignore most style findings. A reviewer asked to find gaps will find some even when the
work is sound; chasing all of them produces over-engineering.
