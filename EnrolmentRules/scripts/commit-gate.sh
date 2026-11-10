#!/usr/bin/env bash
# commit-gate.sh — the commit gate as the hooks run it: verify formatting
# (dotnet format, Prettier), then scripts/all-test.sh. Fix a formatting
# failure with scripts/format.sh.
#
# Called by scripts/pre-commit.sh and scripts/pre-push.sh.

set -euo pipefail

# GUI and hook launches omit the user-local dotnet and Homebrew paths.
export PATH="$HOME/.dotnet:/opt/homebrew/bin:/usr/local/bin:$PATH"

readonly SOLUTION_PATH="EnrolmentRules.slnx"
readonly CLIENT_DIR="src/EnrolmentRules.Web/ClientApp"
readonly FORMAT_TIMEOUT_SECONDS=180
readonly ALL_TEST_TIMEOUT_SECONDS=300

cd "$(dirname "${BASH_SOURCE[0]}")/.."

echo "==> dotnet format ${SOLUTION_PATH} --verify-no-changes"
gtimeout "$FORMAT_TIMEOUT_SECONDS" dotnet format "$SOLUTION_PATH" --verify-no-changes
echo "==> prettier --check (${CLIENT_DIR})"
gtimeout "$FORMAT_TIMEOUT_SECONDS" pnpm --dir "$CLIENT_DIR" format:check
gtimeout "$ALL_TEST_TIMEOUT_SECONDS" ./scripts/all-test.sh
