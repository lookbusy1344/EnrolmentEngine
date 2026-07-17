#!/usr/bin/env bash
#
# Verify the ASP.NET web front end and the Vue ClientApp together. This is the
# focused gate for changes under src/EnrolmentRules.Web or tests/EnrolmentRules.Web.Tests.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
client="$repo/src/EnrolmentRules.Web/ClientApp"

"$here/build-clientapp.sh"

echo "==> building solution with warnings as errors"
dotnet build "$repo/EnrolmentRules.slnx" -warnaserror

echo "==> running web integration tests"
dotnet test "$repo/tests/EnrolmentRules.Web.Tests/EnrolmentRules.Web.Tests.csproj"

echo "==> running ClientApp verify"
pnpm --dir "$client" verify

echo "==> running ClientApp Playwright e2e"
pnpm --dir "$client" e2e

echo "==> PASS: web and ClientApp verification completed"
