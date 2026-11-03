#!/usr/bin/env bash
# Creates an isolated PostgreSQL role + database for KOSHI on an existing PostgreSQL server
# without touching other databases. Idempotent.
# Usage: sudo KOSHI_ENV=/etc/koshi/koshi.env bash provision-db.sh
set -euo pipefail

ENV_FILE="${KOSHI_ENV:-/etc/koshi/koshi.env}"
[[ -f "$ENV_FILE" ]] || { echo "Missing $ENV_FILE"; exit 1; }
DB_PASSWORD="$(grep -E '^KOSHI_DB_PASSWORD=' "$ENV_FILE" | cut -d= -f2-)"
[[ -n "$DB_PASSWORD" ]] || { echo "KOSHI_DB_PASSWORD is empty"; exit 1; }

cd /tmp
id koshi >/dev/null 2>&1 || useradd --system --home-dir /opt/koshi --shell /usr/sbin/nologin koshi

psql_admin() { sudo -u postgres psql -v ON_ERROR_STOP=1 -At "$@"; }

if [[ "$(psql_admin -c "SELECT 1 FROM pg_roles WHERE rolname='koshi'")" != "1" ]]; then
  psql_admin -v pw="$DB_PASSWORD" <<'SQL'
CREATE ROLE koshi LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'pw';
SQL
  echo "role koshi created"
else
  psql_admin -v pw="$DB_PASSWORD" <<'SQL'
ALTER ROLE koshi PASSWORD :'pw';
SQL
  echo "role koshi exists (password synced)"
fi

if [[ "$(psql_admin -c "SELECT 1 FROM pg_database WHERE datname='koshi'")" != "1" ]]; then
  sudo -u postgres createdb -O koshi -E UTF8 koshi
  echo "database koshi created"
fi

psql_admin -c "REVOKE ALL ON DATABASE koshi FROM PUBLIC;"
psql_admin -c "GRANT CONNECT, TEMP ON DATABASE koshi TO koshi;"
psql_admin -d koshi -c "ALTER SCHEMA public OWNER TO koshi;"

echo "--- verification"
psql_admin -c "SELECT datname || ' owner=' || pg_get_userbyid(datdba) FROM pg_database WHERE datname IN ('koshi')"
for db in $(psql_admin -c "SELECT datname FROM pg_database WHERE datname NOT IN ('koshi','template0','template1','postgres')"); do
  echo "koshi can connect to $db: $(psql_admin -c "SELECT has_database_privilege('koshi','$db','CONNECT')")"
done
