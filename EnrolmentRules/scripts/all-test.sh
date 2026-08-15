#!/usr/bin/env bash
#
# Run the full commit gate: the .NET solution build + test, the ClientApp
# checks (lint, no-js-source, typecheck, unit tests, build), and the Playwright
# e2e suite. This script only tests — it never formats and never verifies
# formatting; that is scripts/format.sh's job (dotnet format + Prettier --write).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
client="$repo/src/EnrolmentRules.Web/ClientApp"

echo "==> shutting down dotnet build server (avoids stale-cache bogus errors)"
dotnet build-server shutdown

echo "==> building solution with warnings as errors"
dotnet build "$repo/EnrolmentRules.slnx" -warnaserror

echo "==> running .NET test suite"
dotnet test "$repo/EnrolmentRules.slnx" -v q

echo "==> running ClientApp checks (no formatting verification)"
pnpm --dir "$client" verify:checks

echo "==> running ClientApp Playwright e2e"
pnpm --dir "$client" e2e

echo "==> PASS: full test suite completed"
