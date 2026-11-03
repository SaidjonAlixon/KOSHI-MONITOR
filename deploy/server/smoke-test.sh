#!/usr/bin/env bash
# Post-deploy smoke test through nginx (Host header) without needing public DNS.
HOST="${1:-koshi.uz}"
check() { printf '%-28s -> %s\n' "$1" "$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $HOST" "http://127.0.0.1$1")"; }
for p in / /login /app/devices /api/health /swagger /openapi/v1.json; do check "$p"; done
printf '%-28s -> %s\n' "POST /api/auth/login (bad)" "$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $HOST" -H 'Content-Type: application/json' -d '{"email":"x@y.uz","password":"bad"}' http://127.0.0.1/api/auth/login)"
printf '%-28s -> %s\n' "GET /api/users (anon)" "$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $HOST" http://127.0.0.1/api/users)"
printf '%-28s -> %s\n' "proopex.uz https" "$(curl -s -o /dev/null -w '%{http_code}' -k --resolve proopex.uz:443:127.0.0.1 https://proopex.uz/)"
