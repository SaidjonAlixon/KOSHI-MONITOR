#!/usr/bin/env bash
# Deploys a KOSHI release (API + admin static files) on Ubuntu next to other sites without touching them.
# Usage: sudo bash deploy.sh /tmp/koshi-release.tar.gz
set -euo pipefail

RELEASE="${1:-/tmp/koshi-release.tar.gz}"
ROOT=/opt/koshi
STAGE="$ROOT/staging"
ENV_FILE=/etc/koshi/koshi.env

[[ -f "$RELEASE" ]] || { echo "Release archive $RELEASE not found"; exit 1; }
[[ -f "$ENV_FILE" ]] || { echo "Missing $ENV_FILE (see .env.example)"; exit 1; }
id koshi >/dev/null 2>&1 || useradd --system --home-dir "$ROOT" --shell /usr/sbin/nologin koshi

if ! ldconfig -p | grep -q libicuuc; then
  echo "Installing ICU (required by .NET globalization)"
  apt-get update -qq && apt-get install -y -qq libicu70
fi

install -d -m 755 "$ROOT"
install -d -o koshi -g koshi -m 750 "$ROOT/logs"
install -d -o koshi -g koshi -m 750 "$ROOT/releases"
rm -rf "$STAGE" && install -d "$STAGE"
tar -xzf "$RELEASE" -C "$STAGE"

[[ -n "$(tail -c1 "$ENV_FILE")" ]] && echo >> "$ENV_FILE"
grep -q '^KOSHI_Database__ApplyMigrationsOnStartup=' "$ENV_FILE" || echo 'KOSHI_Database__ApplyMigrationsOnStartup=true' >> "$ENV_FILE"
grep -q '^KOSHI_AgentReleases__Path=' "$ENV_FILE" || echo 'KOSHI_AgentReleases__Path=/opt/koshi/releases' >> "$ENV_FILE"
grep -q '^KOSHI_AgentReleases__SeedInstaller=' "$ENV_FILE" || echo 'KOSHI_AgentReleases__SeedInstaller=/opt/koshi/admin/downloads/KOSHI-Agent-Setup.exe' >> "$ENV_FILE"
chmod 600 "$ENV_FILE"

# Keep the previous release for rollback.
for part in api admin; do
  rm -rf "$ROOT/$part.prev"
  [[ -d "$ROOT/$part" ]] && mv "$ROOT/$part" "$ROOT/$part.prev"
  mv "$STAGE/$part" "$ROOT/$part"
done
chown -R root:koshi "$ROOT/api" && chmod -R u=rwX,g=rX,o= "$ROOT/api" && chmod 750 "$ROOT/api/KOSHI.Api"
chown -R root:root "$ROOT/admin" && chmod -R u=rwX,go=rX "$ROOT/admin"

install -m 644 "$STAGE/server/koshi-api.service" /etc/systemd/system/koshi-api.service
systemctl daemon-reload
systemctl enable --quiet koshi-api
systemctl restart koshi-api

echo "Waiting for API health..."
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:5090/api/health >/dev/null 2>&1; then echo "API healthy"; break; fi
  sleep 2
  if [[ $i -eq 30 ]]; then
    echo "API did not become healthy; rolling back"; journalctl -u koshi-api -n 40 --no-pager
    if [[ -d "$ROOT/api.prev" ]]; then rm -rf "$ROOT/api"; mv "$ROOT/api.prev" "$ROOT/api"; systemctl restart koshi-api; fi
    exit 1
  fi
done

# nginx: install only if the whole configuration still validates, otherwise remove it again.
install -d /var/www/certbot
if [[ ! -f /etc/nginx/sites-available/koshi.conf ]] || ! grep -q "managed by Certbot" /etc/nginx/sites-available/koshi.conf; then
  install -m 644 "$STAGE/server/nginx-koshi.conf" /etc/nginx/sites-available/koshi.conf
fi
# Certbot-managed configs are kept; make sure map tiles (Joylashuv page) are allowed by the CSP.
if ! grep -q "tile.openstreetmap.org" /etc/nginx/sites-available/koshi.conf; then
  sed -i "s#img-src 'self' data: blob:;#img-src 'self' data: blob: https://tile.openstreetmap.org;#g" /etc/nginx/sites-available/koshi.conf
fi
ln -sf /etc/nginx/sites-available/koshi.conf /etc/nginx/sites-enabled/koshi.conf
if nginx -t 2>&1; then
  systemctl reload nginx
  echo "nginx reloaded"
else
  rm -f /etc/nginx/sites-enabled/koshi.conf
  echo "nginx config test failed; KOSHI site disabled, other sites untouched"
  exit 1
fi

rm -rf "$STAGE" "$RELEASE"
echo "KOSHI deployed."
