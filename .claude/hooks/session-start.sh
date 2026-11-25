#!/bin/bash
# Starts the Docker daemon in a Claude Code cloud session: the AppHost and the integration tests need it
# (AGENTS.md, "Docker must be running"), and a fresh cloud container has dockerd installed but not running.
# The environment's setup script cannot start it (no background daemons there), so it starts here, at every
# session start; it does nothing when Docker already answers.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if docker info > /dev/null 2>&1; then
  exit 0
fi

if ! command -v dockerd > /dev/null; then
  echo "session-start: dockerd is not installed; integration tests and the AppHost need Docker." >&2
  exit 0
fi

setsid nohup dockerd > /tmp/dockerd.log 2>&1 < /dev/null &

for _ in $(seq 1 60); do
  if docker info > /dev/null 2>&1; then
    exit 0
  fi
  sleep 1
done

echo "session-start: dockerd did not answer within 60 s; see /tmp/dockerd.log." >&2
exit 0
