#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5099"
LOG_FILE="/tmp/jundiai-healthos.log"

dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" >/dev/null 2>&1 || true' EXIT

for _ in $(seq 1 30); do
  if curl -fsS "$BASE_URL/api/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

curl -fsS "$BASE_URL/api/health" | grep -q 'RCE 008/2026'
curl -fsS "$BASE_URL/api/dashboard" | grep -q 'citizens'
curl -fsS -H 'X-Demo-Role: clinician' "$BASE_URL/api/clinical/workspaces" | grep -q 'Medicina'
curl -fsS "$BASE_URL/acs.html" | grep -q 'ACS Campo'

HTTP_CODE=$(curl -sS -o /tmp/rbac-body.json -w '%{http_code}' -H 'X-Demo-Role: acs' "$BASE_URL/api/sus/production")
if [[ "$HTTP_CODE" != "403" ]]; then
  echo "RBAC smoke failed: expected 403 for ACS reading billing, got $HTTP_CODE"
  cat /tmp/rbac-body.json || true
  exit 1
fi

echo "Smoke POC OK"
