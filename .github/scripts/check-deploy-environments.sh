#!/usr/bin/env bash
# Checks the GitHub environments deploy.yml deploys with, before the first deploy and before real data (#278, README
# "Before the first deploy"): `staging` and `production` each deploy from the branch `main` and the tags `deploy/*` alone,
# so a deploy.yml changed on another branch gets neither their secrets nor their Azure identity; and `production` waits
# for reviewers, never the one who started the run. Prints each problem and exits 1 if there is one.
#
# Usage: bash .github/scripts/check-deploy-environments.sh <owner/repository>
# Needs gh, signed in as someone who may read the repository's environments, and jq.
set -euo pipefail

if [ $# -ne 1 ] || [[ "$1" != */* ]]; then
  echo "Usage: $0 <owner/repository>" >&2
  exit 2
fi

repository="$1"
expected="branch:main tag:deploy/*"
problems=0

problem() {
  echo "$1: $2"
  problems=$((problems + 1))
}

for environment in staging production; do
  if ! settings=$(gh api "repos/$repository/environments/$environment" 2>/dev/null); then
    problem "$environment" "no such environment; create it (Settings, Environments) as the README's \"Before the first deploy\" says."
    continue
  fi

  custom=$(jq -r '.deployment_branch_policy.custom_branch_policies // false' <<<"$settings")
  protected=$(jq -r '.deployment_branch_policy.protected_branches // false' <<<"$settings")
  if [ "$custom" != true ] || [ "$protected" = true ]; then
    problem "$environment" "any branch may deploy to it (deployment branches and tags: \"$(jq -r 'if .deployment_branch_policy == null then "No restriction" elif .deployment_branch_policy.protected_branches then "Protected branches only" else "Selected branches and tags" end' <<<"$settings")\"); select \"Selected branches and tags\" and allow the branch main and the tag pattern deploy/* alone."
  else
    allowed=$(gh api "repos/$repository/environments/$environment/deployment-branch-policies" |
      jq -r '[.branch_policies[] | "\(.type // "branch"):\(.name)"] | sort | join(" ")')
    if [ "$allowed" != "$expected" ]; then
      problem "$environment" "deploys from \"$allowed\"; it should be exactly \"$expected\" (the branch main and the tag pattern deploy/*)."
    fi
  fi

  if [ "$environment" = production ]; then
    reviewers=$(jq -r '[.protection_rules[]? | select(.type == "required_reviewers")] | first // {} | "\((.reviewers // []) | length) \(.prevent_self_review // false)"' <<<"$settings")
    if [ "${reviewers% *}" = 0 ]; then
      problem "$environment" "no required reviewers; a run deploys to production without anyone approving it."
    elif [ "${reviewers#* }" != true ]; then
      problem "$environment" "\"Prevent self-review\" is off; whoever started the run may approve it."
    fi
  fi
done

if [ "$problems" -gt 0 ]; then
  exit 1
fi

echo "Both environments deploy from main and deploy/* alone, and production waits for a reviewer other than the one who started the run."
