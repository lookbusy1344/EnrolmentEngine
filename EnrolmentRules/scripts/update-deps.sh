#!/usr/bin/env bash
#
# Update dependencies: NuGet packages (via the dotnet-outdated global tool) and the
# ClientApp's pnpm modules.
#
# By default both ecosystems are kept within their current major version, mirroring pnpm's
# normal behaviour of respecting the caret/tilde range already in package.json (NuGet has no
# such range concept, so dotnet-outdated is run with -vl Minor to get the same effect). Pass
# --major to allow major-version bumps in both ecosystems instead.
#
# This only updates lockfiles/.csproj versions and reports what changed; run the commit gates
# (README/CLAUDE.md) afterwards before committing.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
client="$repo/src/EnrolmentRules.Web/ClientApp"

allow_major=false
for arg in "$@"; do
	case "$arg" in
	--major) allow_major=true ;;
	*)
		echo "usage: $(basename "$0") [--major]" >&2
		exit 1
		;;
	esac
done

echo "==> checking for dotnet-outdated"
if ! dotnet tool list -g | grep -q '^dotnet-outdated-tool '; then
	echo "==> installing dotnet-outdated-tool (global)"
	dotnet tool install --global dotnet-outdated-tool
fi

echo "==> updating NuGet packages"
if [ "$allow_major" = true ]; then
	dotnet outdated "$repo/EnrolmentRules.slnx" -u
else
	dotnet outdated "$repo/EnrolmentRules.slnx" -u -vl Minor
fi

pnpm_update_args=()
if [ "$allow_major" = true ]; then
	pnpm_update_args+=(--latest)
fi

echo "==> updating pnpm modules"
# ${arr[@]+"${arr[@]}"} (not the bare "${arr[@]}") avoids bash 3.2's unbound-variable error under
# set -u when the array is empty -- macOS ships bash 3.2 as /bin/bash.
pnpm --dir "$client" update ${pnpm_update_args[@]+"${pnpm_update_args[@]}"}

echo "==> remaining outdated pnpm packages (major bumps need a manual package.json edit)"
pnpm --dir "$client" outdated || true

echo "==> PASS: dependencies updated; run the commit gates before committing"
