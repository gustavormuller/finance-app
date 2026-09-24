# Deferred — questions, deviations, skipped items

Every entry: spec · checkpoint · what · why deferred · what was done instead.

## Run setup (orchestrator)

- **run · setup · `docs/CLAUDE_CODE_WORKFLOW.md` does not exist.** The run prompt says to read
  it. Nothing to read; proceeded with CLAUDE.md, ARCHITECTURE.md and the specs.
- **run · setup · `docs/handoffs/003.md` does not exist**, and neither did `docs/handoffs/`. The
  prompt says handoffs follow 003.md's shape. Shape taken from the prompt instead: status, what
  exists now, conventions, decisions the next spec must make, known debts, lessons.
- **run · setup · specs define no checkpoints.** The orchestrator split each spec into
  checkpoints along its own sections (domain + data model → application/API → web → E2E),
  keeping each near the ~200-line diff guideline. The split is listed per spec in STATUS.md.
- **run · setup · cloud Linux environment, not Windows.** .NET SDK 10.0.401 (pinned by
  global.json) installed from packages.microsoft.com's Debian 12 feed into /opt/dotnet,
  because builds.dotnet.microsoft.com is blocked by the egress proxy. Playwright expected
  Chromium build 1243; the preinstalled build 1194 was symlinked in its place. E2E runs against
  database `financas_e2e` via user-secrets (Google keys are dummies; E2E uses dev-login).
  None of this is committed — it is environment, not repo.
- **run · setup · orchestrator read the specs just-in-time.** ARCHITECTURE.md ADRs, 004 and
  005 were read up front; 006–010 are read when each spec starts, to keep the orchestrator's
  context small. Each subagent reads its own spec in full.
