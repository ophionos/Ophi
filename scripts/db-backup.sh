#!/usr/bin/env bash
# Dump the compose PostgreSQL database to one custom-format file, then check that pg_restore can
# read it. The stack keeps running; pg_dump takes a consistent snapshot.
#
#   scripts/db-backup.sh [output-file]       default: backups/ophi-<UTC timestamp>.dump
#
# OPHI_COMPOSE overrides the compose command (default: docker compose -f docker/docker-compose.yml,
# run from the repo root). Use sudo when your user cannot reach the Docker socket.
set -euo pipefail

cd "$(dirname "$0")/.."
read -r -a compose <<< "${OPHI_COMPOSE:-docker compose -f docker/docker-compose.yml}"
service="${OPHI_DB_SERVICE:-db}"

out="${1:-backups/ophi-$(date -u +%Y%m%dT%H%M%SZ).dump}"
mkdir -p "$(dirname "$out")"
tmp="$out.partial"
trap 'rm -f "$tmp"' EXIT

"${compose[@]}" exec -T "$service" pg_dump -U ophi -d ophi --format=custom > "$tmp"

# A truncated or empty dump fails here instead of on the day it is needed. This reads every data
# block; `pg_restore --list` reads only the table of contents and passes a truncated file.
"${compose[@]}" exec -T "$service" pg_restore --file=/dev/null < "$tmp"

mv "$tmp" "$out"
trap - EXIT
echo "Backup written: $out ($(du -h "$out" | cut -f1))"
