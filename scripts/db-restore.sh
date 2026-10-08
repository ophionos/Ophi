#!/usr/bin/env bash
# Restore a db-backup.sh dump into the compose PostgreSQL database.
#
#   scripts/db-restore.sh --check <file>    restore into a scratch database, print row counts, drop it
#   scripts/db-restore.sh <file> [--yes]    replace the live database with the dump
#
# Both modes first restore into the scratch database `ophi_restore`, so a broken dump never touches
# the live data. A real restore then stops api and worker, renames the live database to
# `ophi_before_restore_<timestamp>` (kept for rollback; drop it yourself), renames the scratch
# database to `ophi`, and starts api and worker again.
#
# OPHI_COMPOSE overrides the compose command (default: docker compose -f docker/docker-compose.yml,
# run from the repo root). Use sudo when your user cannot reach the Docker socket.
set -euo pipefail

usage() { echo "usage: $0 --check <file> | $0 <file> [--yes]" >&2; exit 2; }

check=false yes=false file=
for arg in "$@"; do
	case "$arg" in
		--check) check=true ;;
		--yes) yes=true ;;
		-*) usage ;;
		*) [[ -z "$file" ]] || usage; file="$arg" ;;
	esac
done
[[ -n "$file" ]] || usage
[[ -f "$file" ]] || { echo "No such file: $file" >&2; exit 1; }
file="$(cd "$(dirname "$file")" && pwd)/$(basename "$file")"

cd "$(dirname "$0")/.."
read -r -a compose <<< "${OPHI_COMPOSE:-docker compose -f docker/docker-compose.yml}"
service="${OPHI_DB_SERVICE:-db}"
scratch=ophi_restore

# `exec -T` forwards stdin, so psql gets /dev/null; otherwise it swallows the confirmation answer.
psql() {
	"${compose[@]}" exec -T -e PGOPTIONS='-c client_min_messages=warning' "$service" \
		psql -U ophi -d "${PGDB:-postgres}" -v ON_ERROR_STOP=1 -qAt "$@" < /dev/null
}

drop_scratch() { psql -c "DROP DATABASE IF EXISTS $scratch WITH (FORCE)" > /dev/null; }
trap drop_scratch EXIT

drop_scratch
psql -c "CREATE DATABASE $scratch OWNER ophi"
"${compose[@]}" exec -T "$service" pg_restore -U ophi -d "$scratch" \
	--no-owner --exit-on-error --single-transaction < "$file"

echo "Restored into $scratch. Rows per table:"
PGDB=$scratch psql -P format=aligned -P tuples_only=off -c "
	SELECT table_schema || '.' || table_name AS \"table\",
	       (xpath('/row/c/text()', query_to_xml(
	           format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text::bigint AS \"rows\"
	FROM information_schema.tables
	WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
	ORDER BY 1"
echo "Latest migration: $(PGDB=$scratch psql -c 'SELECT max("MigrationId") FROM "__EFMigrationsHistory"')"

if $check; then
	echo "Check passed. The live database was not changed."
	exit 0
fi

if ! $yes; then
	read -r -p "Replace the live database 'ophi' with this dump? api and worker restart. [y/N] " reply || reply=
	[[ "$reply" == [yY] ]] || { echo "Cancelled. The live database was not changed."; exit 1; }
fi

backup_name="ophi_before_restore_$(date -u +%Y%m%d_%H%M%S)"
"${compose[@]}" stop api worker
trap '"${compose[@]}" start api worker' EXIT
# Nothing else should be connected now; terminate stragglers (a psql session, a metrics scraper).
psql -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity
         WHERE datname IN ('ophi', '$scratch') AND backend_type = 'client backend'
           AND pid <> pg_backend_pid()" > /dev/null
# One -c string is one transaction: both renames happen, or neither does.
psql -c "ALTER DATABASE ophi RENAME TO $backup_name; ALTER DATABASE $scratch RENAME TO ophi"
"${compose[@]}" start api worker
trap - EXIT

echo "Restore complete. The previous database is kept as '$backup_name'."
echo "Drop it when you no longer need it:"
echo "  ${compose[*]} exec $service psql -U ophi -d postgres -c 'DROP DATABASE $backup_name'"
