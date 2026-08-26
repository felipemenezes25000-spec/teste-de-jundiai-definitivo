#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5100"
LOG_FILE="/tmp/jundiai-healthos-platform.log"

dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" >/dev/null 2>&1 || true' EXIT

for _ in $(seq 1 35); do
  if curl -fsS "$BASE_URL/api/health/live" >/dev/null 2>&1; then break; fi
  sleep 1
done

assert_json(){ local expression="$1"; python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"; }

curl -fsS "$BASE_URL/api/health/live" | assert_json 'd["status"]=="live"'
curl -fsS "$BASE_URL/api/health/ready" | assert_json 'd["status"]=="ready" and d["pocFallbackAllowed"] is True'

LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN")

CITIZENS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/citizens")
CITIZEN_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$CITIZENS")

# Persistência opcional deve ficar fail-safe quando banco não está configurado.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/persistence/readiness" | assert_json 'd["configured"] is False and d["mode"]=="poc-memory-fallback"'
HTTP_CODE=$(curl -sS -o /tmp/persistence-checkpoint.json -w '%{http_code}' -X POST "${AUTH[@]}" "$BASE_URL/api/audit/persistence/checkpoint" -H 'Content-Type: application/json' -d '{"label":"should-fail-without-db"}')
[[ "$HTTP_CODE" == "409" ]] || { echo "Expected 409 checkpoint without DB, got $HTTP_CODE"; cat /tmp/persistence-checkpoint.json; exit 1; }

# Correlation ID deve ser preservado quando válido.
HEADERS=$(mktemp)
curl -fsS -D "$HEADERS" -o /dev/null -H 'X-Correlation-Id: smoke-platform-001' "$BASE_URL/api/health/live"
grep -qi 'X-Correlation-Id: smoke-platform-001' "$HEADERS"

# LGPD / break-glass / revogação / exportação do titular.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit/privacy/readiness" | assert_json 'd["policies"]>=6'
GRANT=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/privacy/break-glass" -H 'Content-Type: application/json' -d "{\"citizenId\":\"$CITIZEN_ID\",\"actor\":\"admin.jundiai\",\"reason\":\"smoke de acesso emergencial\",\"minutes\":10}")
GRANT_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="active" and len(d["accessTokenHash"])==64; print(d["id"])' <<<"$GRANT")
REVOKED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/privacy/break-glass/$GRANT_ID/revoke" -H 'Content-Type: application/json' -d '{"actor":"admin.jundiai","reason":"fim do smoke"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="revoked" and d["revokedAt"]' <<<"$REVOKED"
EXPORT=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/audit/privacy/subject-export" -H 'Content-Type: application/json' -d "{\"citizenId\":\"$CITIZEN_ID\",\"actor\":\"admin.jundiai\",\"purpose\":\"direito do titular - smoke\"}")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d["sha256"])==64 and d["contentType"]=="application/json" and d["payloadJson"]' <<<"$EXPORT"

# Telemetria deve refletir chamadas feitas durante o próprio smoke.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/operations/telemetry" | assert_json 'd["totalRequests"]>=10 and len(d["groups"])>=3'

# Evidence Pack: executa 14 blocos, cruza Contract Pack, dependências e Evidence Ledger e produz hash canônico.
PACK=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/evidence-pack" -H 'Content-Type: application/json' -d '{"actor":"smoke.platform","reRunVerification":true}')
PACK_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d["packageSha256"])==64; p=d["payload"]; assert p["verification"]["passedBlocks"]==14 and p["verification"]["totalBlocks"]==14; assert len(p["blocks"])==14; assert any(x["id"]=="HAB-AT-29" for x in p["nonCodeBlockers"]); assert p["persistence"]["configured"] is False; print(p["packId"])' <<<"$PACK")
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/evidence-pack/latest/manifest" | assert_json 'd["passedBlocks"]==14 and d["totalBlocks"]==14 and d["indexedBlocks"]==14 and len(d["packageSha256"])==64 and d["packageHashValid"] is True and d["ledgerChainValid"] is True'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/evidence-pack/latest/verify" | assert_json 'd["packageHashValid"] is True and d["ledgerChainValid"] is True and d["demonstrationIntegrityReady"] is True and d["passedBlocks"]==14'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/evidence-pack/latest/export" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["payload"]["packId"]==sys.argv[1] and len(d["packageSha256"])==64' "$PACK_ID"

# Preflight da banca: cenário ouro + runner + Evidence Pack + assets + ledger + integrações + blocker documental.
PRESENTATION=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/presentation/prepare" -H 'Content-Type: application/json' -d '{"actor":"smoke.presentation"}')
PRESENTATION_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["ready"] is True and d["status"]=="ready"; assert d["passedBlocks"]==14 and d["totalBlocks"]==14; assert len(d["checks"])==8 and all(x["passed"] for x in d["checks"]); assert len(d["pages"])==22 and all(x["exists"] and x["bytes"]>0 for x in d["pages"]); assert len(d["assets"])==10 and all(x["exists"] and x["bytes"]>0 for x in d["assets"]); assert len(d["evidencePackSha256"])==64; assert d["persistenceMode"]=="poc-memory-fallback"; assert any(x["id"]=="HAB-AT-29" for x in d["nonCodeBlockers"]); print(d["id"])' <<<"$PRESENTATION")
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/presentation/latest" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["id"]==sys.argv[1] and d["ready"] is True and d["passedBlocks"]==14' "$PRESENTATION_ID"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/presentation/checklist" | assert_json 'len(d["pages"])==22 and all(x["exists"] for x in d["pages"]) and len(d["assets"])==10 and all(x["exists"] for x in d["assets"])'

# Evidence Ledger recebeu controles de privacidade, runner, pacote e preflight de apresentação.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/ledger" | assert_json 'any(x["action"]=="privacy.break-glass.open" for x in d) and any(x["action"]=="privacy.subject-export.generate" for x in d) and any(x["action"]=="poc.run.complete" for x in d) and any(x["action"]=="poc.evidence-pack.generate" for x in d) and any(x["action"]=="poc.presentation.prepare" for x in d)'

echo "Smoke plataforma + Evidence Pack + apresentação READY OK"
