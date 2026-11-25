#!/usr/bin/env bash
# Red first for a pull request (docs/adr): runs check-red-first.sh on the files the pull request itself changes.
# Usage: red-first.sh <pull request body file> <base> <head> <author>
# <base> is the base branch as it is now (origin/<base branch>), never the event's base.sha: the checked-out merge ref is
# built from the base as it is now, and a diff from an older base would count the commits merged into it since as the
# pull request's own (#429). A dependency update by Dependabot (manifests and lockfiles only) quotes no failing test; the
# base branch's dependency-update.sh decides it, the pull request's copy only while the base has none.
set -euo pipefail

body="$1"
base="$2"
head="$3"
author="$4"

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

exemption=.github/scripts/board/dependency-update.sh
git show "$base:$exemption" > "$work/dependency-update.sh" 2>/dev/null || cp "$here/board/dependency-update.sh" "$work/dependency-update.sh"
if note="$(bash "$work/dependency-update.sh" "$author" "$base" "$head")"; then
  echo "::notice title=red-first::$note"
  exit 0
fi

git diff --name-only "$base...$head" > "$work/changed.txt"
bash "$here/check-red-first.sh" "$body" "$work/changed.txt"
