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
# europe-west1 (Belgium) is a Tier 1 pricing region, so the Always Free allowance goes
# further than in europe-west2 (London) — see docs/deployment.md "Cost after the free trial".
region="${REGION:-europe-west1}"
# Cloud Run never deletes a revision on its own, so every deploy leaves one behind. Idle revisions
# cost nothing under scale-to-zero, but keep only a few for rollback rather than letting them
# accumulate indefinitely.
revision_keep_count=3
revision_file="$repo/.sourcerevision"
project="$(gcloud config get-value project)"
# The project hosting JobTrack's persistent jobtrack-web-pg deployment. This script takes its
# target from ambient gcloud config, so a stale config would silently redeploy this public demo
# back alongside the persistent deployment's Cloud SQL instance, secrets, and key ring -- the
# exact co-tenancy risk JobTrack/docs/plans/2026-08-06-cloudrun-persistent-isolation-plan.md
# closed (#2.1/#2.3). Refuse it; the demos live in jobtrack-demo-projects.
persistent_project="project-e2ce9938-0f7b-48a8-b0d"
if [ "$project" = "$persistent_project" ]; then
	echo "ERROR: gcloud config points at $persistent_project, which hosts the persistent" >&2
	echo "jobtrack-web-pg deployment. Switch to the demo project first:" >&2
	echo "  gcloud config set project jobtrack-demo-projects" >&2
	exit 1
fi
# Deliberately no roles: this demo reads no secret, bucket, or database, so it needs nothing
# beyond what its own image carries. Kept off the default compute service account, which
# typically holds project-wide roles (e.g. cloudbuild.builds.builder) that would let a
# compromise of this public demo reach a co-tenant persistent deployment's resources -- see
# JobTrack/docs/plans/2026-08-06-cloudrun-persistent-isolation-plan.md #2.1.
demo_service_account="demo-run@$project.iam.gserviceaccount.com"

trap 'rm -f "$revision_file"' EXIT

commit="$(git rev-parse --short=10 HEAD)"
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
	commit="$commit-dirty"
fi
printf '%s\n' "$commit" >"$revision_file"

if ! gcloud iam service-accounts describe "$demo_service_account" --project="$project" >/dev/null 2>&1; then
	echo "==> creating $demo_service_account (no roles, deliberately)"
	gcloud iam service-accounts create demo-run \
		--project="$project" \
		--display-name="Disposable demo services (no roles, deliberately)"
fi

echo "==> deploying $service to $region, stamped $commit"

# --max-instances 1 and --session-affinity are not optional decoration: sessions are
# in-memory and per-instance. See docs/deployment.md "Session state and scaling".
gcloud run deploy "$service" \
	--source . \
	--region "$region" \
	--allow-unauthenticated \
	--max-instances 1 \
	--session-affinity \
	--service-account "$demo_service_account"

# Newest-first, so tail keeps everything past revision_keep_count. gcloud refuses to delete a
# revision carrying live traffic, so this never targets the one just deployed.
echo "==> pruning old revisions, keeping the $revision_keep_count most recent"
while read -r stale_revision; do
	[[ -n $stale_revision ]] || continue
	gcloud run revisions delete "$stale_revision" \
		--project="$project" --region="$region" --quiet >/dev/null 2>&1 || true
done < <(gcloud run revisions list \
	--service="$service" --project="$project" --region="$region" \
	--sort-by='~metadata.creationTimestamp' --format='value(metadata.name)' |
	tail -n "+$((revision_keep_count + 1))")

# --source deploys push a new image to this Artifact Registry repo on every run; it is
# shared with jobtrack-web (see the persistent_project guard above), so scope pruning to
# this service's own image only -- never touch jobtrack-web's.
image="$region-docker.pkg.dev/$project/cloud-run-source-deploy/$service"
echo "==> pruning old $service images, keeping the $revision_keep_count most recent"
while read -r stale_digest; do
	[[ -n $stale_digest ]] || continue
	gcloud artifacts docker images delete "$image@$stale_digest" \
		--project="$project" --quiet >/dev/null 2>&1 || true
done < <(gcloud artifacts docker images list "$image" \
	--project="$project" --format='value(version)' --sort-by='~CREATE_TIME' |
	tail -n "+$((revision_keep_count + 1))")
