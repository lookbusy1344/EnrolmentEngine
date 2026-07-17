#!/usr/bin/env bash
#
# Deploy EnrolmentRules.Web to Cloud Run, stamped with the commit it was built from.
#
# Why this script exists rather than a bare `gcloud run deploy --source .`: the build
# cannot reach git. The git root is the *parent* monorepo directory while the Docker build
# context is this folder, so no .git is ever inside the context — nothing .dockerignore can
# change — and `--source .` offers no way to pass a Docker build arg. So the hash is written
# into the context as .sourcerevision, which Directory.Build.props' StampGitCommit reads when
# git is unavailable. Without it the footer reports "0.1.0+unknown".
#
# .sourcerevision is generated, gitignored, and deleted on exit; it is deliberately absent
# from .gcloudignore/.dockerignore so it reaches the build.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
cd "$repo"

service="${SERVICE:-enrolment-web}"
region="${REGION:-europe-west2}"
revision_file="$repo/.sourcerevision"

trap 'rm -f "$revision_file"' EXIT

commit="$(git rev-parse --short=10 HEAD)"
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
	commit="$commit-dirty"
fi
printf '%s\n' "$commit" >"$revision_file"

echo "==> deploying $service to $region, stamped $commit"

# --max-instances 1 and --session-affinity are not optional decoration: sessions are
# in-memory and per-instance. See docs/deployment.md "Session state and scaling".
gcloud run deploy "$service" \
	--source . \
	--region "$region" \
	--allow-unauthenticated \
	--max-instances 1 \
	--session-affinity
