#!/bin/bash
# Starts the Docker daemon in a Claude Code cloud session: the AppHost and the integration tests need it
# (AGENTS.md, "Docker must be running"), and a fresh cloud container has dockerd installed but not running.
# The environment's setup script cannot start it (no background daemons there), so it starts here, at every
# session start; it does nothing when Docker already answers.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Each probe is bounded, so a daemon that accepts the connection but hangs cannot stretch the 60 s wait.
docker_answers() {
  timeout 5 docker info > /dev/null 2>&1
}

if ! command -v docker > /dev/null || ! command -v dockerd > /dev/null; then
  echo "session-start: docker or dockerd is not installed; integration tests and the AppHost need Docker." >&2
  exit 0
fi

if docker_answers; then
  exit 0
fi

# A fresh file of the hook's own, not a fixed /tmp path that a symlink could redirect.
if ! log="$(mktemp -t dockerd.XXXXXX.log)"; then
  echo "session-start: could not create a log file for dockerd; Docker was not started." >&2
  exit 0
fi
setsid nohup dockerd > "$log" 2>&1 < /dev/null &
daemon=$!

deadline=$((SECONDS + 60))
while [ "$SECONDS" -lt "$deadline" ]; do
  if docker_answers; then
    exit 0
  fi
  if ! kill -0 "$daemon" 2> /dev/null; then
    echo "session-start: dockerd exited at once; see $log." >&2
    exit 0
  fi
  sleep 1
done

echo "session-start: dockerd did not answer within 60 s; see $log." >&2
exit 0
