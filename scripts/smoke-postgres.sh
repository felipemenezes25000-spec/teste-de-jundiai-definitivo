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

curl -fsS "$BASE_URL/api/health/ready" >/dev/null || { echo "Aplicação PostgreSQL não iniciou"; cat "$LOG_FILE"; exit 1; }
assert_json(){ local expression="$1"; python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"; }

curl -fsS "$BASE_URL/api/health/ready" | assert_json 'd["status"]=="ready" and d["database"]=="durable-postgresql" and d["pocFallbackAllowed"] is False'

LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN" -H 'X-Institution-Id: jundiai-ci' -H 'X-Health-Unit-Id: UBS-CI')

READINESS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/readiness")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["configured"] is True and d["canConnect"] is True and d["mode"]=="durable-postgresql" and len(d["pendingMigrations"])==0' <<<"$READINESS"

# Checkpoint resumido existente.
CHECKPOINT=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoint" -H 'Content-Type: application/json' -d '{"label":"ci-postgres-checkpoint"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["institutionId"]=="jundiai-ci" and d["healthUnitId"]=="UBS-CI" and d["envelopeCount"]>=7 and d["checkpointId"]' <<<"$CHECKPOINT"

# Checkpoint completo: os principais bounded contexts precisam entrar no mesmo checkpoint institucional.
FULL=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoints/full" -H 'Content-Type: application/json' -d '{"label":"ci-full-domain-checkpoint"}')
FULL_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["institutionId"]=="jundiai-ci" and d["healthUnitId"]=="UBS-CI"; assert d["envelopeCount"]>=19; assert len(d["manifestSha256"])==64; print(d["checkpointId"])' <<<"$FULL")

MANIFEST=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoints/$FULL_ID/manifest")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["checkpointId"]==sys.argv[1]; assert len(d["entries"])>=19; assert len(d["manifestSha256"])==64; kinds={x["kind"] for x in d["entries"]}; required={"citizens-master","scheduling-bookings","clinical-orders","diagnostics-orders","immunizations-history","pharmacy-dispensations","referrals","sus-production-v2","evidence-ledger"}; assert required.issubset(kinds), (required-kinds)' "$FULL_ID" <<<"$MANIFEST"

DRILL=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/recovery-drill" -H 'Content-Type: application/json' -d "{\"checkpointId\":\"$FULL_ID\",\"actor\":\"ci.recovery\"}")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["checkpointId"]==sys.argv[1]; assert d["integrityValid"] is True; assert d["restorePreviewValid"] is True; assert d["criticalKindsPresent"]==d["criticalKindsExpected"]; assert d["envelopeCount"]>=19; assert len(d["failures"])==0' "$FULL_ID" <<<"$DRILL"

curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/recovery/readiness" | assert_json 'd["configured"] is True and d["recoveryDrillAvailable"] is True and d["checkpoints"]>=2'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoints" | assert_json 'len(d)>=2 and any(x["envelopeCount"]>=19 for x in d)'

# Inbox idempotente: a mesma mensagem externa só gera um receipt durável.
INBOX_FIRST=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/inbox" -H 'Content-Type: application/json' -d '{"type":"lis.result.demo","messageId":"lis-msg-001","payload":{"accession":"ACC-001","result":"hash-safe"},"actor":"ci.integration","idempotencyRetentionDays":30}')
INBOX_RECEIPT=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["duplicate"] is False and len(d["payloadSha256"])==64; print(d["receiptId"])' <<<"$INBOX_FIRST")
INBOX_SECOND=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/inbox" -H 'Content-Type: application/json' -d '{"type":"lis.result.demo","messageId":"lis-msg-001","payload":{"accession":"ACC-001","result":"hash-safe"},"actor":"ci.integration","idempotencyRetentionDays":30}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["duplicate"] is True and d["receiptId"]==sys.argv[1]' "$INBOX_RECEIPT" <<<"$INBOX_SECOND"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/inbox" | assert_json 'len(d)==1 and d[0]["resourceId"]=="lis.result.demo:lis-msg-001"'

# Outbox + replay idempotente + processamento normal.
FIRST=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"rnds.document.demo","idempotencyKey":"ci-outbox-001","payload":{"citizen":"demo","document":"hash-only"}}')
OUTBOX_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="pending" and d["idempotentReplay"] is False; print(d["id"])' <<<"$FIRST")
SECOND=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"rnds.document.demo","idempotencyKey":"ci-outbox-001","payload":{"citizen":"demo","document":"hash-only"}}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["id"]==sys.argv[1] and d["idempotentReplay"] is True' "$OUTBOX_ID" <<<"$SECOND"

PROCESSED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$OUTBOX_ID/processed" -H 'Content-Type: application/json' -d '{}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="processed" and d["processedAt"]' <<<"$PROCESSED"

# Retry/dead-letter: falhas sucessivas devem parar a mensagem e requeue exige justificativa.
RETRY_MSG=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"pacs.study.demo","idempotencyKey":"ci-outbox-retry-001","payload":{"study":"1.2.3.demo"}}')
RETRY_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["id"])' <<<"$RETRY_MSG")
FAIL1=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$RETRY_ID/failure" -H 'Content-Type: application/json' -d '{"errorCode":"PACS_TIMEOUT","errorClass":"transient","maxAttempts":2,"actor":"ci.worker"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="retry" and d["attempts"]==1' <<<"$FAIL1"
FAIL2=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$RETRY_ID/failure" -H 'Content-Type: application/json' -d '{"errorCode":"PACS_TIMEOUT","errorClass":"transient","maxAttempts":2,"actor":"ci.worker"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="dead_letter" and d["attempts"]==2' <<<"$FAIL2"
REQUEUED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$RETRY_ID/requeue" -H 'Content-Type: application/json' -d '{"actor":"ci.operator","reason":"dependência externa recuperada no cenário de teste"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="pending" and d["attempts"]==2' <<<"$REQUEUED"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/pending" | assert_json 'any(x["id"]=="'"$RETRY_ID"'" and x["status"]=="pending" for x in d)'

curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/messaging/readiness" | assert_json 'd["configured"] is True and d["inboxReceipts"]==1 and d["pendingOutbox"]>=1'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" | assert_json 'any(x["status"]=="processed" for x in d)'

# Recovery e integração durável precisam deixar trilha de evidência.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/ledger" | assert_json 'any(x["action"]=="persistence.full-checkpoint" for x in d) and any(x["action"]=="persistence.recovery-drill" for x in d) and any(x["action"]=="integration.inbox.accept" for x in d) and any(x["action"]=="integration.outbox.failure" for x in d) and any(x["action"]=="integration.outbox.requeue" for x in d)'

echo "Smoke PostgreSQL + recovery + messaging OK · checkpoint=$FULL_ID"
