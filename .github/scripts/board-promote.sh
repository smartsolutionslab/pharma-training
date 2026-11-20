#!/usr/bin/env bash
# Releases issues to the board once their dependencies are done: labels `agent:ready` on every open issue that
#   - has a `lane:` label, no assignee, and none of `agent:ready`, `agent:claimed`, `agent:blocked` or `needs:*`;
#   - has a `## Depends on` section whose first paragraph names at least one issue (`#12`, `owner/repo#34`);
#   - and every issue named there is closed as completed.
# An issue whose section names no issue is left to a developer (it says so in the log).
#
# A stop-gap until the board protocol knows dependencies (smartsolutionslab/software-fabric#849). It is hand-written,
# not generated, so `fabric upgrade` leaves it alone; drop it once base.fabric brings its own.
# Uses `gh api` (REST) only, like the board scripts: cloud sessions refuse GraphQL.
# Usage: board-promote.sh [owner/repo]   (DRY_RUN=1 prints what it would do)
set -euo pipefail

repo="${1:-${GITHUB_REPOSITORY:-$(gh repo view --json nameWithOwner --jq .nameWithOwner)}}"
dry_run="${DRY_RUN:-}"

declare -A state_of

# Prints "closed" for an issue closed as completed, otherwise its state (open, or closed:not_planned).
dependency_state() {
  local ref="$1"
  if [ -z "${state_of[$ref]:-}" ]; then
    local owner_repo="${ref%#*}" number="${ref##*#}"
    state_of[$ref]="$(gh api "repos/$owner_repo/issues/$number" \
      --jq 'if .state == "closed" and (.state_reason // "completed") == "completed" then "closed"
            elif .state == "closed" then "closed:\(.state_reason)" else .state end')"
  fi
  echo "${state_of[$ref]}"
}

candidates="$(gh api --paginate "repos/$repo/issues?state=open&per_page=100" --jq '
  .[] | select(has("pull_request") | not)
      | [.labels[].name] as $names
      | select(any($names[]; startswith("lane:")))
      | select((.assignees | length) == 0)
      | select(any($names[]; . == "agent:ready" or . == "agent:claimed" or . == "agent:blocked" or startswith("needs:")) | not)
      | .number')"

promoted=0
for issue in $candidates; do
  body="$(gh api "repos/$repo/issues/$issue" --jq '.body // ""')"
  # The dependencies are the section's first paragraph; prose after a blank line explains them and may name issues
  # that are not dependencies ("#55 (X1) checks the real interplay").
  section="$(awk '/^## Depends on[[:space:]]*$/ { inside = 1; next }
    inside && (/^## / || /^---/) { exit }
    inside && /^[[:space:]]*$/ { if (seen) exit; next }
    inside { seen = 1; print }' <<< "$body")"
  refs="$(grep -oE '([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#[0-9]+' <<< "$section" | sed -E "s|^#|$repo#|" | sort -u || true)"
  if [ -z "$refs" ]; then
    echo "#$issue: its '## Depends on' section names no issue; left to a developer."
    continue
  fi

  open=()
  for ref in $refs; do
    state="$(dependency_state "$ref")"
    [ "$state" = "closed" ] || open+=("$ref ($state)")
  done
  if [ "${#open[@]}" -gt 0 ]; then
    echo "#$issue: waiting for ${open[*]}."
    continue
  fi

  named="$(sed -E "s|^$repo#|#|" <<< "$refs" | paste -sd, - | sed 's/,/, /g')"
  if [ -n "$dry_run" ]; then
    echo "#$issue: would release (dependencies done: $named)."
    continue
  fi
  gh api -X POST "repos/$repo/issues/$issue/labels" -f 'labels[]=agent:ready' > /dev/null
  gh api -X POST "repos/$repo/issues/$issue/comments" -f body="<!-- board:promote -->
Released: all dependencies ($named) are done, so \`agent:ready\` is on." > /dev/null
  echo "#$issue: released (dependencies done: $named)."
  promoted=$((promoted + 1))
done

echo "Released $promoted issue(s)."
