#!/usr/bin/env bash
#
# The single command that proves the repository is healthy.
# Runs in order and fails fast. If this exits 0, the repo is green.
#
# Requires: .NET 10 SDK, Node 20.19+/22+, and a running Docker daemon
# (Testcontainers starts a real PostgreSQL for the integration tests).

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }

step "dotnet build"
dotnet build FinanceApp.slnx --nologo

# Do not pass --nologo to `dotnet test`. The .NET 10 SDK forwards it into the
# Microsoft.Testing.Platform test host, which aborts host startup and reports
# "Zero tests ran" (exit 5) instead of an argument error.
step "dotnet test (unit + integration; Testcontainers starts PostgreSQL)"
dotnet test FinanceApp.slnx --no-build

cd web

step "web: dependencies"
if [ ! -d node_modules ]; then
  npm ci
else
  echo "node_modules present, skipping install"
fi

step "web: typecheck"
npm run typecheck

step "web: lint"
npm run lint

step "web: unit tests"
npm run test:run

printf '\n\033[1;32m==> verify.sh: all checks passed\033[0m\n'
