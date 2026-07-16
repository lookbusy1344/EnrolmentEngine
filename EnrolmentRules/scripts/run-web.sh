#!/usr/bin/env bash
#
# Build and launch EnrolmentRules.Web for local debugging and manual testing.
#
# ASP.NET Core sets the content root to the project's *source* directory for
# `dotnet run`, but workflows/ and data/ only exist in the build *output*
# (copied there via <Content Include> in the .csproj). So this launches the
# compiled executable directly from its own output directory instead.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
project="$repo/src/EnrolmentRules.Web/EnrolmentRules.Web.csproj"
out="$repo/src/EnrolmentRules.Web/bin/Debug/net10.0"

echo "==> building EnrolmentRules.Web"
dotnet build "$project"

echo "==> launching on http://localhost:5299"
cd "$out"
ASPNETCORE_URLS="http://localhost:5299" ASPNETCORE_ENVIRONMENT=Development ./EnrolmentRules.Web
