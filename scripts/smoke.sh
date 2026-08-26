#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5099"
LOG_FILE="/tmp/jundiai-healthos.log"

dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" >/dev/null 2>&1 || true' EXIT

for _ in $(seq 1 35); do
  if curl -fsS "$BASE_URL/api/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

assert_json() {
  local expression="$1"
  python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"
}

curl -fsS "$BASE_URL/api/health" | grep -q 'RCE 008/2026'
for page in / /citizen.html /operations.html /esus.html /acs.html /login.html /poc.html /caretrace.html /agenda.html /diagnostics.html /dental-v2.html /billing-v2.html /governance.html; do
  curl -fsS "$BASE_URL$page" >/dev/null
done
curl -fsS "$BASE_URL/poc.html" | grep -q '14 blocos'
curl -fsS "$BASE_URL/caretrace.html" | grep -q 'CareTrace'
curl -fsS "$BASE_URL/agenda.html" | grep -q 'Agenda'
curl -fsS "$BASE_URL/diagnostics.html" | grep -q 'Diagn'
curl -fsS "$BASE_URL/dental-v2.html" | grep -q 'Odont'
curl -fsS "$BASE_URL/billing-v2.html" | grep -q 'Faturamento SUS v2'
curl -fsS "$BASE_URL/governance.html" | grep -q 'Govern'

# Autenticação + MFA
LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="mfa_required"; print(d["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" \
  -H 'Content-Type: application/json' \
  -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="authenticated" and d["mfaVerified"] is True; print(d["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN")
curl -fsS "${AUTH[@]}" "$BASE_URL/api/auth/me" | assert_json 'd["userName"]=="admin.jundiai" and d["role"]=="poc_admin"'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/security/readiness" | assert_json 'd["seededUsers"]>=9 and d["lockout"]["attempts"]==5'

CITIZENS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/citizens")
CITIZEN_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d)>0; print(d[0]["id"])' <<<"$CITIZENS")
CITIZEN_NAME=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["name"])' <<<"$CITIZENS")
CITIZEN_UNIT=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["healthUnit"])' <<<"$CITIZENS")

UNITS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/units")
UNIT_COUNT=$(python3 -c 'import json,sys; print(len(json.load(sys.stdin)))' <<<"$UNITS")
[[ "$UNIT_COUNT" == "58" ]] || { echo "Expected 58 demo health units, got $UNIT_COUNT"; exit 1; }
UNIT_CODE=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["code"])' <<<"$UNITS")

# RBAC negativo permanece obrigatório
HTTP_CODE=$(curl -sS -o /tmp/rbac-body.json -w '%{http_code}' -H 'X-Demo-Role: acs' "$BASE_URL/api/sus/production")
[[ "$HTTP_CODE" == "403" ]] || { echo "Expected 403 for ACS billing, got $HTTP_CODE"; cat /tmp/rbac-body.json; exit 1; }

# Contract Pack / 14 blocos / industrialização
CONTRACT=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/contract/jundiai/readiness")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d["blocks"])==14; assert d["overallScore"]>=80' <<<"$CONTRACT"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/contract/platform/readiness" | assert_json 'len(d["productionGates"])>=10 and d["currentPoc"]["status"]=="POC"'

# Agenda centralizada
SCHED=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/scheduling/readiness")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["gridCount"]>=5 and d["slotCount"]>0 and d["quotaCount"]>=5' <<<"$SCHED"
SLOTS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/scheduling/slots")
SLOT_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); x=next(i for i in d if not i["blocked"] and i["booked"] < i["capacity"]); print(x["id"])' <<<"$SLOTS")
BOOK=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/scheduling/book" -H 'Content-Type: application/json' \
  -d "{\"slotId\":\"$SLOT_ID\",\"citizenId\":\"$CITIZEN_ID\",\"citizenName\":\"$CITIZEN_NAME\",\"priority\":\"routine\",\"source\":\"smoke\"}")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="scheduled"' <<<"$BOOK"

# Porta Digital e safety kernel
ASSESSMENT=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/evaluate" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"chiefComplaint\":\"dor forte no joelho ha tres dias\",\"age\":57,\"pregnant\":false}")
ASSESSMENT_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$ASSESSMENT")
HANDOFF=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/$ASSESSMENT_ID/handoff" \
  -H 'Content-Type: application/json' \
  -d '{"specialty":"Ortopedia","consentAccepted":true}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["appointmentId"]' <<<"$HANDOFF"
HANDOFF_2=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/$ASSESSMENT_ID/handoff" \
  -H 'Content-Type: application/json' \
  -d '{"specialty":"Ortopedia","consentAccepted":true}')
python3 -c 'import json,sys; a=json.loads(sys.argv[1]); b=json.load(sys.stdin); assert a["appointmentId"]==b["appointmentId"]' "$HANDOFF" <<<"$HANDOFF_2"

EMERGENCY=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/evaluate" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"chiefComplaint\":\"dor no peito e falta de ar intensa\",\"age\":57,\"pregnant\":false}")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["riskLevel"]=="emergency" and len(d["redFlags"])>0' <<<"$EMERGENCY"

# Cenário ouro idempotente
GOLDEN=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/scenarios/golden-path" -H 'Content-Type: application/json' -d '{}')
GOLDEN_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="completed" and d["evidenceChainValid"] is True and len(d["artifacts"])>=6; print(d["id"])' <<<"$GOLDEN")
GOLDEN_AGAIN=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/scenarios/golden-path" -H 'Content-Type: application/json' -d '{}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["id"]==sys.argv[1]' "$GOLDEN_ID" <<<"$GOLDEN_AGAIN"

# Telemedicina / documentos / IA / CareTrace gerados pelo cenário
curl -fsS "${AUTH[@]}" "$BASE_URL/api/telemedicine/sessions" | assert_json 'len(d)>=1 and any(x["status"]=="completed" for x in d)'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/documents" | assert_json 'len(d)>=1 and any(x["status"]=="signed_demo" for x in d)'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/ai/decisions" | assert_json 'len(d)>=1 and any(x["reviewStatus"]=="approved" for x in d)'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/care-trace/$CITIZEN_ID" | assert_json 'len(d["nodes"])>=2 and len(d["edges"])>=1 and "continuity" in d'

# Diagnóstico avançado
curl -fsS "${AUTH[@]}" "$BASE_URL/api/diagnostics/v2/readiness" | assert_json 'd["orderCount"]>=1 and "resultado crítico com ciência" in d["capabilities"]'

# Recepção
CHECKIN=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/ubs/reception/checkin" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"unitCode\":\"$UNIT_CODE\",\"service\":\"Consulta APS\",\"priority\":\"routine\",\"notes\":\"smoke\"}")
TICKET_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$CHECKIN")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/ubs/reception/$TICKET_ID/call" \
  -H 'Content-Type: application/json' -d '{"room":"Sala 03","professional":"Enf. Smoke"}' | grep -q 'called'

# Vacinação
VACCINE_LOTS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/lots")
VACCINE_LOT_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$VACCINE_LOTS")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/administer" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"vaccineLotId\":\"$VACCINE_LOT_ID\",\"dose\":\"Dose smoke\",\"route\":\"IM\",\"site\":\"Deltoide\",\"professional\":\"Enf. Smoke\",\"professionalCouncil\":\"COREN-SP DEMO\"}" | grep -q 'Dose smoke'

# Farmácia base + estoque avançado
INVENTORY=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/pharmacy/inventory")
INVENTORY_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$INVENTORY")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/dispense" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"inventoryLotId\":\"$INVENTORY_ID\",\"quantity\":1,\"prescriptionReference\":\"RX-SMOKE\",\"professional\":\"Farm. Smoke\"}" | grep -q 'dispense'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/inventory/v2/readiness" | assert_json 'd["lots"]>=3 and d["alerts"]>=1'

# Almoxarifado
WAREHOUSE=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/warehouse")
WAREHOUSE_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$WAREHOUSE")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/warehouse/transfer" \
  -H 'Content-Type: application/json' \
  -d "{\"warehouseItemId\":\"$WAREHOUSE_ID\",\"quantity\":1,\"destinationUnitCode\":\"$UNIT_CODE\",\"reference\":\"REQ-SMOKE\",\"actor\":\"Warehouse Smoke\"}" | grep -q 'transfer'

# Digitalização
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/records/digitized" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"barcode\":null,\"documentType\":\"Prontuario smoke\",\"pages\":2,\"sourceUnit\":\"Smoke Unit\",\"storageReference\":\"BOX-SMOKE\",\"actor\":\"Arquivo Smoke\"}" | grep -q 'barcode'

# APS / e-SUS demonstrativo
curl -fsS -X POST -H 'X-Demo-Role: acs' "$BASE_URL/api/psf/esus/individuals" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"socialName\":null,\"raceColor\":\"Nao informado\",\"education\":\"Nao informado\",\"occupation\":\"Nao informado\",\"hasDisability\":false,\"isPregnant\":false,\"isBedridden\":false,\"chronicConditions\":[\"Hipertensao\"],\"acsName\":\"ACS Smoke\"}" | grep -q 'ACS Smoke'

# Odontologia avançada gera produção SUS
curl -fsS -X PUT "${AUTH[@]}" "$BASE_URL/api/dental/v2/$CITIZEN_ID/teeth/16/surfaces/O" -H 'Content-Type: application/json' \
  -d '{"condition":"caries","notes":"smoke","professional":"Dra. Smoke"}' | grep -q 'caries'
DENTAL=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/dental/v2/$CITIZEN_ID/procedures" -H 'Content-Type: application/json' \
  -d "{\"citizenName\":\"$CITIZEN_NAME\",\"healthUnit\":\"$CITIZEN_UNIT\",\"sigtapCode\":\"0307030032\",\"description\":\"Restauracao smoke\",\"tooth\":16,\"sextant\":null,\"surfaces\":[\"O\"],\"cid\":\"K02.9\",\"professional\":\"Dra. Smoke\",\"professionalCouncil\":\"CRO DEMO\"}")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["procedure"]["tooth"]==16 and d["production"]["procedureCode"]=="0307030032"' <<<"$DENTAL"

# Billing avançado: crítica -> fechamento -> exportação -> reabertura
PRODUCTION=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/sus/billing/v2/production")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d)>=3' <<<"$PRODUCTION"
COMPETENCE=$(date +%Y%m)
BATCH=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/sus/billing/v2/batches" -H 'Content-Type: application/json' -d "{\"competence\":\"$COMPETENCE\"}")
BATCH_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="validated" and len(d["issues"])==0; print(d["id"])' <<<"$BATCH")
CLOSED=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/sus/billing/v2/batches/$BATCH_ID/close" -H 'Content-Type: application/json' -d '{}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="closed" and len(d["exportChecksum"])==64' <<<"$CLOSED"
EXPORT=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/sus/billing/v2/batches/$BATCH_ID/export")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["format"]=="POC-BPA-STRUCTURED" and len(d["sha256"])==64 and len(d["lines"])>=2' <<<"$EXPORT"
REOPEN=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/sus/billing/v2/batches/$BATCH_ID/reopen" -H 'Content-Type: application/json' -d '{"reason":"smoke de versionamento","actor":"smoke"}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="draft" and d["version"]==2' <<<"$REOPEN"

# Migração, integrações, operação, analytics, governança e plataforma
curl -fsS "${AUTH[@]}" "$BASE_URL/api/migration/readiness" | assert_json 'd["batches"]>=1'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/integrations/readiness" | assert_json 'd["total"]>=10 and d["homologated"]==0 and d["productionEnabled"]==0'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/operations/readiness" | assert_json 'd["trainingSessions"]>=3 and d["serviceDesk"]["total"]>=1'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/analytics/executive" | assert_json 'd["network"]["healthUnits"]==58'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/ai/readiness" | assert_json 'd["policies"]>=6'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/platform/readiness" | assert_json 'len(d["productionGates"])>=10'

# Evidência e auditoria finais
curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/verify" | assert_json 'd["valid"] is True and d["checkedEvents"]>=1'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/dashboard" | grep -q 'citizens'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/audit" | grep -q 'warehouse.transfer'

echo "Smoke POC consolidado v2 OK"
