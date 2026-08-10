#!/usr/bin/env bash
#
# Build the Vue enrolment app into src/EnrolmentRules.Web/wwwroot/app.
# The output directory is generated and gitignored; commit ClientApp source and
# pnpm-lock.yaml, not the hashed Vite bundle.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
client="$repo/src/EnrolmentRules.Web/ClientApp"

echo "==> restoring ClientApp packages"
pnpm --dir "$client" install --frozen-lockfile

echo "==> building Vue ClientApp into wwwroot/app"
pnpm --dir "$client" run build

echo "==> built $repo/src/EnrolmentRules.Web/wwwroot/app/manifest.json"
