---
name: ship-feature
description: Session B — implement a spec from specs/ test-first, running verify.sh before claiming done. Requires an approved spec.
disable-model-invocation: true
---

# Session B — Build

Run this in a fresh session, normal mode. It implements one spec and nothing else.

Implement `specs/NNN-<name>.md` (ask the user which, if they did not say). If the spec
does not exist, stop and say so — do not invent one.

Work test-first, in this order:

1. Write the tests from the spec's test plan. Do not write implementation yet.
2. Run them. Confirm they fail for the right reason. Show the output.
3. Commit the tests.
4. Implement until they pass. Do not modify the tests to make them pass — if a test is
   wrong, stop and explain why.
5. Run `bash scripts/verify.sh` and show the full output.
6. Commit.

Stop and ask before:

- applying any migration (show the generated SQL first)
- creating more than 3 new files in one step
- deviating from the spec in any way

## Multi-tenancy check

If this feature adds an entity, add an integration test that creates two users, writes
data as user A, and asserts user B receives none of it. That test is mandatory for
every user-owned entity.

## Frontend work

After the UI is implemented, use Playwright to drive the browser through the flow
described in the spec, take a screenshot, and show it to the user. Compare against the
spec's described behaviour and list any differences.

## Reminders

- Money is `decimal`. Never `double` or `float`, not even in a local.
- `Domain/` must not reference EF Core or `HttpClient`.
- No Repository over EF Core — `Application/` uses `AppDbContext` directly.
- Keep the diff under ~200 lines. If it needs more, the task was too big: split it and
  tell the user.
