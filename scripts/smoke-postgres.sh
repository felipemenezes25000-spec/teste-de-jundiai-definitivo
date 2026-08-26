#!/usr/bin/env bash
set -euo pipefail

: "${JUNDIAI_POSTGRES_PASSWORD:?JUNDIAI_POSTGRES_PASSWORD obrigatoria}"
BASE_URL="http://127.0.0.1:5101"
LOG_FILE="/tmp/jundiai-healthos-postgres.log"
CONNECTION="Host=127.0.0.1;Port=${JUNDIAI_POSTGRES_PORT:-5432};Database=${JUNDIAI_POSTGRES_DB:-jundiai};Username=${JUNDIAI_POSTGRES_USER:-jundiai};Password=${JUNDIAI_POSTGRES_PASSWORD}"
CURRENT_STAGE="bootstrap"

stage(){ CURRENT_STAGE="$1"; echo "[postgres-smoke] $CURRENT_STAGE"; }
cleanup(){
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "[postgres-smoke] FALHA em: $CURRENT_STAGE"
    echo "[postgres-smoke] últimas linhas da aplicação:"
    tail -n 120 "$LOG_FILE" 2>/dev/null || true
  fi
  kill "$APP_PID" >/dev/null 2>&1 || true
  exit "$status"
}

ConnectionStrings__Jundiai="$CONNECTION" Jundiai__Persistence__AutoMigrate=true \
  dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
trap cleanup EXIT

for _ in $(seq 1 45); do
  if curl -fsS "$BASE_URL/api/health/ready" >/dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS "$BASE_URL/api/health/ready" >/dev/null || { echo "Aplicação PostgreSQL não iniciou"; exit 1; }

assert_json(){
  local label="$1"
  local expression="$2"
  python3 -c 'import json,sys; d=json.load(sys.stdin); label=sys.argv[1]; expr=sys.argv[2]; ok=eval(expr,{"__builtins__":{}},{"d":d,"any":any,"len":len}); assert ok, f"{label}: {json.dumps(d,ensure_ascii=False)[:1800]}"' "$label" "$expression"
}

stage "health readiness"
curl -fsS "$BASE_URL/api/health/ready" | assert_json "health" 'd["status"]=="ready" and d["database"]=="durable-postgresql" and d["pocFallbackAllowed"] is False'

stage "login e MFA"
LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("challengeId"), d; print(d["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("sessionToken"), d; print(d["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN" -H 'X-Institution-Id: jundiai-ci' -H 'X-Health-Unit-Id: UBS-CI')

stage "PostgreSQL migrations/readiness"
READINESS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/readiness")
printf '%s' "$READINESS" | assert_json "persistence-readiness" 'd["configured"] is True and d["canConnect"] is True and d["mode"]=="durable-postgresql" and len(d["pendingMigrations"])==0'

stage "checkpoint básico"
CHECKPOINT=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoint" -H 'Content-Type: application/json' -d '{"label":"ci-postgres-checkpoint"}')
printf '%s' "$CHECKPOINT" | assert_json "basic-checkpoint" 'd["institutionId"]=="jundiai-ci" and d["healthUnitId"]=="UBS-CI" and d["envelopeCount"]>=7 and bool(d["checkpointId"])'

stage "checkpoint completo"
FULL=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoints/full" -H 'Content-Type: application/json' -d '{"label":"ci-full-domain-checkpoint"}')
FULL_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("institutionId")=="jundiai-ci", d; assert d.get("healthUnitId")=="UBS-CI", d; assert d.get("envelopeCount",0)>=19, d; assert len(d.get("manifestSha256",""))==64, d; print(d["checkpointId"])' <<<"$FULL")

echo "[postgres-smoke] full checkpoint=$FULL_ID"

stage "manifesto SHA-256"
MANIFEST=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoints/$FULL_ID/manifest")
python3 -c 'import json,sys; d=json.load(sys.stdin); cid=sys.argv[1]; assert d.get("checkpointId")==cid, d; assert len(d.get("entries",[]))>=19, d; assert len(d.get("manifestSha256",""))==64, d; kinds={x.get("kind") for x in d["entries"]}; required={"citizens-master","scheduling-bookings","clinical-orders","diagnostics-orders","immunizations-history","pharmacy-dispensations","referrals","sus-production-v2","evidence-ledger"}; missing=required-kinds; assert not missing, f"kinds ausentes={missing}; kinds={kinds}"' "$FULL_ID" <<<"$MANIFEST"

stage "recovery drill"
DRILL=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/recovery-drill" -H 'Content-Type: application/json' -d "{\"checkpointId\":\"$FULL_ID\",\"actor\":\"ci.recovery\"}")
python3 -c 'import json,sys; d=json.load(sys.stdin); cid=sys.argv[1]; assert d.get("checkpointId")==cid, d; assert d.get("integrityValid") is True, d; assert d.get("restorePreviewValid") is True, d; assert d.get("criticalKindsPresent")==d.get("criticalKindsExpected"), d; assert d.get("envelopeCount",0)>=19, d; assert len(d.get("failures",[]))==0, d' "$FULL_ID" <<<"$DRILL"

stage "recovery readiness e catálogo de checkpoints"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/recovery/readiness" | assert_json "recovery-readiness" 'd["configured"] is True and d["recoveryDrillAvailable"] is True and d["checkpoints"]>=2'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoints" | assert_json "checkpoint-list" 'len(d)>=2 and any(x["envelopeCount"]>=19 for x in d)'

stage "inbox idempotente"
INBOX_FIRST=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/inbox" -H 'Content-Type: application/json' -d '{"type":"lis.result.demo","messageId":"lis-msg-001","payload":{"accession":"ACC-001","result":"hash-safe"},"actor":"ci.integration","idempotencyRetentionDays":30}')
INBOX_RECEIPT=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("duplicate") is False, d; assert len(d.get("payloadSha256",""))==64, d; print(d["receiptId"])' <<<"$INBOX_FIRST")
INBOX_SECOND=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/inbox" -H 'Content-Type: application/json' -d '{"type":"lis.result.demo","messageId":"lis-msg-001","payload":{"accession":"ACC-001","result":"hash-safe"},"actor":"ci.integration","idempotencyRetentionDays":30}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("duplicate") is True, d; assert d.get("receiptId")==sys.argv[1], d' "$INBOX_RECEIPT" <<<"$INBOX_SECOND"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/inbox" | assert_json "inbox-list" 'len(d)==1 and d[0]["resourceId"]=="lis.result.demo:lis-msg-001"'

stage "outbox idempotente processado"
FIRST=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"rnds.document.demo","idempotencyKey":"ci-outbox-001","payload":{"citizen":"demo","document":"hash-only"}}')
OUTBOX_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("status")=="pending" and d.get("idempotentReplay") is False, d; print(d["id"])' <<<"$FIRST")
SECOND=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"rnds.document.demo","idempotencyKey":"ci-outbox-001","payload":{"citizen":"demo","document":"hash-only"}}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("id")==sys.argv[1] and d.get("idempotentReplay") is True, d' "$OUTBOX_ID" <<<"$SECOND"
PROCESSED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$OUTBOX_ID/processed" -H 'Content-Type: application/json' -d '{}')
printf '%s' "$PROCESSED" | assert_json "outbox-processed" 'd["status"]=="processed" and bool(d["processedAt"])'

stage "outbox retry/dead-letter/requeue"
RETRY_MSG=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" -H 'Content-Type: application/json' -d '{"type":"pacs.study.demo","idempotencyKey":"ci-outbox-retry-001","payload":{"study":"1.2.3.demo"}}')
RETRY_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("id"), d; print(d["id"])' <<<"$RETRY_MSG")
FAIL1=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$RETRY_ID/failure" -H 'Content-Type: application/json' -d '{"errorCode":"PACS_TIMEOUT","errorClass":"transient","maxAttempts":2,"actor":"ci.worker"}')
printf '%s' "$FAIL1" | assert_json "outbox-failure-1" 'd["status"]=="retry" and d["attempts"]==1'
FAIL2=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$RETRY_ID/failure" -H 'Content-Type: application/json' -d '{"errorCode":"PACS_TIMEOUT","errorClass":"transient","maxAttempts":2,"actor":"ci.worker"}')
printf '%s' "$FAIL2" | assert_json "outbox-failure-2" 'd["status"]=="dead_letter" and d["attempts"]==2'
REQUEUED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/$RETRY_ID/requeue" -H 'Content-Type: application/json' -d '{"actor":"ci.operator","reason":"dependência externa recuperada no cenário de teste"}')
printf '%s' "$REQUEUED" | assert_json "outbox-requeue" 'd["status"]=="pending" and d["attempts"]==2'
PENDING=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox/pending")
python3 -c 'import json,sys; d=json.load(sys.stdin); rid=sys.argv[1]; assert any(x.get("id")==rid and x.get("status")=="pending" for x in d), d' "$RETRY_ID" <<<"$PENDING"

stage "messaging readiness"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/messaging/readiness" | assert_json "messaging-readiness" 'd["configured"] is True and d["inboxReceipts"]==1 and d["pendingOutbox"]>=1'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/outbox" | assert_json "outbox-list" 'any(x["status"]=="processed" for x in d)'

stage "evidence ledger de recovery/messaging"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/ledger" | assert_json "evidence-actions" 'any(x["action"]=="persistence.full-checkpoint" for x in d) and any(x["action"]=="persistence.recovery-drill" for x in d) and any(x["action"]=="integration.inbox.accept" for x in d) and any(x["action"]=="integration.outbox.failure" for x in d) and any(x["action"]=="integration.outbox.requeue" for x in d)'

stage "concluído"
echo "Smoke PostgreSQL + recovery + messaging OK · checkpoint=$FULL_ID"
