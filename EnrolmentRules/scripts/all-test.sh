#!/usr/bin/env bash
#
# Run the full commit gate: the .NET solution build/format/test, the ClientApp
# verify gate (lint, format:check, no-js-source, typecheck, unit tests, build), and
# the Playwright e2e suite.
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

echo "==> running ClientApp verify"
pnpm --dir "$client" verify

echo "==> running ClientApp Playwright e2e"
pnpm --dir "$client" e2e

echo "==> PASS: full test suite completed"
