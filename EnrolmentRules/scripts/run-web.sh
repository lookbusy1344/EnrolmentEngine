#!/usr/bin/env bash
#
# Watch, build, and launch EnrolmentRules.Web for local debugging and manual testing.
# Rebuilds automatically on change: C#/Razor via dotnet watch, Vue via vite build --watch.
#
# Two content-root/web-root subtleties, both worked around with environment variables:
#
#   ASPNETCORE_CONTENTROOT — ASP.NET Core sets the content root to the project's *source*
#   directory for `dotnet run`/`dotnet watch run`, but workflows/ and data/ only exist in the
#   build *output* (copied there via <Content Include> in the .csproj). Program.cs resolves both
#   directories from builder.Environment.ContentRootPath, so pointing this at the build output
#   fixes it without launching the compiled executable directly (which can't be watched).
#
#   ASPNETCORE_WEBROOT — with the content root in bin/, the web root would default to
#   bin/Debug/net10.0/wwwroot, which only refreshes on a full build. Pointing it back at the
#   source wwwroot lets the app serve vite build --watch's output the instant it lands.
#   ViteManifestReader re-reads manifest.json per request, so a Vue edit needs no dotnet
#   restart — just a browser refresh.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
project="$repo/src/EnrolmentRules.Web/EnrolmentRules.Web.csproj"
out="$repo/src/EnrolmentRules.Web/bin/Debug/net10.0"
client="$repo/src/EnrolmentRules.Web/ClientApp"
webroot="$repo/src/EnrolmentRules.Web/wwwroot"

# Build the bundle up front so /app is never stale on first launch, then keep rebuilding it.
# dotnet watch can't do this job: its hot-reload path sees a .vue edit, finds no managed code
# change and never rebuilds, while --no-hot-reload races Vite's content-hashed filenames.
"$here/build-clientapp.sh"

echo "==> watching ClientApp (vite build --watch)"
# `pnpm run` is only a wrapper: killing it orphans the vite process it spawned, which keeps
# watching and rewriting wwwroot/app after Ctrl+C. Kill its children first, then the wrapper.
pnpm --dir "$client" run build:watch >/dev/null &
vite_pid=$!
trap 'pkill -P "$vite_pid" 2>/dev/null || true; kill "$vite_pid" 2>/dev/null || true' EXIT INT TERM

echo "==> watching EnrolmentRules.Web on http://localhost:5299 (rebuilds on change)"
ASPNETCORE_URLS="http://localhost:5299" \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_CONTENTROOT="$out" \
ASPNETCORE_WEBROOT="$webroot" \
dotnet watch run --project "$project" --non-interactive
