#!/usr/bin/env bash
#
# Apply every formatter in the repo, in one pass: dotnet format for C#/Razor, Prettier for the Vue
# ClientApp (TypeScript, .vue, CSS, JSON, Markdown).
#
# This applies fixes rather than checking for them — run it before the commit gates, which is where
# the equivalent --verify-no-changes / format:check runs live (see CLAUDE.md). Running an IDE
# formatter over this repo instead is what this script exists to avoid: Rider's defaults disagree
# with Prettier on the ClientApp sources (indent width, import spacing) and reformatting there
# silently breaks `pnpm format:check`.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
client="$repo/src/EnrolmentRules.Web/ClientApp"

echo "==> formatting C# and Razor (dotnet format)"
dotnet format "$repo/EnrolmentRules.slnx"

echo "==> formatting ClientApp sources (prettier --write)"
pnpm --dir "$client" run format

echo "==> PASS: formatters applied; run the commit gates next"
