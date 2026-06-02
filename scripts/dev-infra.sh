#!/usr/bin/env bash
# Start local Postgres + Redis for SlackTube in ONE container (dev convenience).
#
# postgres:16-alpine is the base; redis is installed via `apk add` and run as a
# background daemon next to postgres (postgres stays PID 1 so the container's life
# tracks the DB). The apk trick is used because pulling the official redis image hangs
# on some networks (Docker blob CDN blocked); the Alpine package CDN is reachable.
#
# Trade-off vs separate containers: simpler to start/stop, but mixed logs and a Redis
# crash is invisible (postgres keeps the container alive). Fine for local dev; in
# production run them as separate services.
set -euo pipefail

NAME=slacktube-deps

start() {
  docker rm -f "$NAME" >/dev/null 2>&1 || true

  echo "→ starting Postgres + Redis in one container ($NAME): :5432 + :6379"
  docker run -d --name "$NAME" \
    -e POSTGRES_USER=slacktube -e POSTGRES_PASSWORD=slacktube -e POSTGRES_DB=slacktube \
    -p 5432:5432 -p 6379:6379 \
    --entrypoint sh postgres:16-alpine -c '
      apk add --no-cache redis >/dev/null &&
      redis-server --bind 0.0.0.0 --protected-mode no --daemonize yes &&
      exec docker-entrypoint.sh postgres' >/dev/null

  echo -n "→ waiting for readiness"
  for _ in $(seq 1 40); do
    pg=$(docker exec "$NAME" pg_isready -U slacktube 2>/dev/null | grep -c accepting || true)
    rd=$(docker exec "$NAME" sh -c 'redis-cli ping 2>/dev/null' 2>/dev/null | grep -c PONG || true)
    if [ "$pg" = "1" ] && [ "$rd" = "1" ]; then
      echo " ready."; docker ps --format '  {{.Names}} {{.Status}} {{.Ports}}'; return 0
    fi
    echo -n "."; sleep 1
  done
  echo " timed out waiting for the container." >&2; exit 1
}

stop() {
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  echo "stopped + removed $NAME"
}

case "${1:-start}" in
  start) start ;;
  stop) stop ;;
  *) echo "usage: $0 [start|stop]"; exit 1 ;;
esac
