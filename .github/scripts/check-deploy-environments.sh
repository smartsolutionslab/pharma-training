#!/usr/bin/env bash
# Checks the GitHub environments deploy.yml deploys with, before the first deploy and before real data (#278, README
# "Before the first deploy"): `staging` and `production` each deploy from the branch `main` and the tags `deploy/*` alone,
# so a deploy.yml changed on another branch gets neither their secrets nor their Azure identity; neither lets an
# administrator bypass those rules; `production` waits for reviewers, never the one who started the run; and an active tag
# ruleset restricts creating, moving and deleting `deploy/*` tags to its bypass list, since whoever can push such a tag
# could otherwise deploy a deploy.yml of their own. Prints each problem and exits 1 if there is one.
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
errors=$(mktemp)
trap 'rm -f "$errors"' EXIT

problem() {
  echo "$1: $2"
  problems=$((problems + 1))
}

for environment in staging production; do
  if ! settings=$(gh api "repos/$repository/environments/$environment" 2>"$errors"); then
    if grep -q 'HTTP 404' "$errors"; then
      problem "$environment" "no such environment; create it (Settings, Environments) as the README's \"Before the first deploy\" says."
    else
      problem "$environment" "could not be read ($(tr '\n' ' ' <"$errors")); sign gh in as someone who may read the repository's environments."
    fi
    continue
  fi

  if [ "$(jq -r '.can_admins_bypass == false' <<<"$settings")" != true ]; then
    problem "$environment" "administrators may bypass its rules; turn off \"Allow administrators to bypass configured protection rules\"."
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

# An active tag ruleset whose conditions take in every deploy/* tag and exclude none, restricting creation, update and
# deletion. Only those listed in the rulesets (not their details) are fetched one by one.
deploy_tags_protected=false
for id in $(gh api "repos/$repository/rulesets" | jq -r '.[] | select(.target == "tag" and .enforcement == "active") | .id'); do
  if gh api "repos/$repository/rulesets/$id" | jq -e '
      (.conditions.ref_name.include // []) as $include
      | (.conditions.ref_name.exclude // []) as $exclude
      | ($include | any(. == "refs/tags/deploy/*" or . == "~ALL"))
        and ($exclude | length == 0)
        and ((["creation", "update", "deletion"] - [.rules[]?.type]) | length == 0)' >/dev/null; then
    deploy_tags_protected=true
  fi
done

if [ "$deploy_tags_protected" != true ]; then
  problem "deploy/* tags" "no active tag ruleset restricts creating, moving and deleting them (Settings, Rules, Rulesets; target refs/tags/deploy/*, no exclusions); whoever can push a deploy/* tag could deploy a deploy.yml of their own."
fi

if [ "$problems" -gt 0 ]; then
  exit 1
fi

echo "Both environments deploy from main and deploy/* alone, without an administrator's bypass; production waits for a reviewer other than the one who started the run; and only the tag ruleset's bypass list may create, move or delete deploy/* tags."
