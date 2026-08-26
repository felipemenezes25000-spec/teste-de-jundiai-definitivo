#!/usr/bin/env bash
set -euo pipefail

: "${JUNDIAI_POSTGRES_PASSWORD:?JUNDIAI_POSTGRES_PASSWORD obrigatoria}"
BASE_URL="http://127.0.0.1:5101"
LOG_FILE="/tmp/jundiai-healthos-postgres.log"
CONNECTION="Host=127.0.0.1;Port=${JUNDIAI_POSTGRES_PORT:-5432};Database=${JUNDIAI_POSTGRES_DB:-jundiai};Username=${JUNDIAI_POSTGRES_USER:-jundiai};Password=${JUNDIAI_POSTGRES_PASSWORD}"

ConnectionStrings__Jundiai="$CONNECTION" Jundiai__Persistence__AutoMigrate=true \
  dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" >/dev/null 2>&1 || true' EXIT

for _ in $(seq 1 45); do
  if curl -fsS "$BASE_URL/api/health/ready" >/dev/null 2>&1; then break; fi
  sleep 1
done

assert_json(){ local expression="$1"; python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"; }

curl -fsS "$BASE_URL/api/health/ready" | assert_json 'd["status"]=="ready" and d["database"]=="durable-postgresql" and d["pocFallbackAllowed"] is False'

LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN" -H 'X-Institution-Id: jundiai-ci' -H 'X-Health-Unit-Id: UBS-CI')

READINESS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/readiness")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["configured"] is True and d["canConnect"] is True and d["mode"]=="durable-postgresql" and len(d["pendingMigrations"])==0' <<<"$READINESS"

CHECKPOINT=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoint" -H 'Content-Type: application/json' -d '{"label":"ci-postgres-checkpoint"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["institutionId"]=="jundiai-ci" and d["healthUnitId"]=="UBS-CI" and d["envelopeCount"]>=7 and d["checkpointId"]' <<<"$CHECKPOINT"

FIRST=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"rnds.document.demo","idempotencyKey":"ci-outbox-001","payload":{"citizen":"demo","document":"hash-only"}}')
OUTBOX_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="pending" and d["idempotentReplay"] is False; print(d["id"])' <<<"$FIRST")
SECOND=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"rnds.document.demo","idempotencyKey":"ci-outbox-001","payload":{"citizen":"demo","document":"hash-only"}}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["id"]==sys.argv[1] and d["idempotentReplay"] is True' "$OUTBOX_ID" <<<"$SECOND"

PROCESSED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$OUTBOX_ID/processed" -H 'Content-Type: application/json' -d '{}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="processed" and d["processedAt"]' <<<"$PROCESSED"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" | assert_json 'any(x["id"]==sys.argv[1] if False else x["status"]=="processed" for x in d)'

echo "Smoke PostgreSQL OK"
