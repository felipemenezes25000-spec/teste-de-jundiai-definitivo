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
curl -fsS "$BASE_URL/" | grep -q 'Jundiaí HealthOS'
curl -fsS "$BASE_URL/citizen.html" | grep -q 'Porta Digital'
curl -fsS "$BASE_URL/operations.html" | grep -q 'Operação integrada'
curl -fsS "$BASE_URL/esus.html" | grep -q 'Território e fichas e-SUS'
curl -fsS "$BASE_URL/acs.html" | grep -q 'ACS Campo'

CITIZENS=$(curl -fsS "$BASE_URL/api/citizens")
CITIZEN_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$CITIZENS")

UNITS=$(curl -fsS "$BASE_URL/api/units")
UNIT_COUNT=$(python3 -c 'import json,sys; print(len(json.load(sys.stdin)))' <<<"$UNITS")
if [[ "$UNIT_COUNT" != "58" ]]; then
  echo "Expected 58 demo health units, got $UNIT_COUNT"
  exit 1
fi
UNIT_CODE=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["code"])' <<<"$UNITS")

curl -fsS -H 'X-Demo-Role: clinician' "$BASE_URL/api/clinical/workspaces" | grep -q 'Medicina'
curl -fsS -H 'X-Demo-Role: clinician' "$BASE_URL/api/clinical/patients/$CITIZEN_ID/summary" | grep -q 'timeline'

HTTP_CODE=$(curl -sS -o /tmp/rbac-body.json -w '%{http_code}' -H 'X-Demo-Role: acs' "$BASE_URL/api/sus/production")
if [[ "$HTTP_CODE" != "403" ]]; then
  echo "RBAC smoke failed: expected 403 for ACS reading billing, got $HTTP_CODE"
  cat /tmp/rbac-body.json || true
  exit 1
fi

ASSESSMENT=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/evaluate" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"chiefComplaint\":\"dor forte no joelho ha tres dias\",\"age\":57,\"pregnant\":false}")
ASSESSMENT_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$ASSESSMENT")
HANDOFF=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/$ASSESSMENT_ID/handoff" \
  -H 'Content-Type: application/json' \
  -d '{"specialty":"Ortopedia","consentAccepted":true}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["appointmentId"]' <<<"$HANDOFF"

EMERGENCY=$(curl -fsS -X POST "$BASE_URL/api/citizen/intelligent-access/evaluate" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"chiefComplaint\":\"dor no peito e falta de ar intensa\",\"age\":57,\"pregnant\":false}")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["riskLevel"]=="emergency" and len(d["redFlags"])>0' <<<"$EMERGENCY"

CHECKIN=$(curl -fsS -X POST "$BASE_URL/api/ubs/reception/checkin" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"unitCode\":\"$UNIT_CODE\",\"service\":\"Consulta APS\",\"priority\":\"routine\",\"notes\":\"smoke\"}")
TICKET_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$CHECKIN")
curl -fsS -X POST "$BASE_URL/api/ubs/reception/$TICKET_ID/call" \
  -H 'Content-Type: application/json' \
  -d '{"room":"Sala 03","professional":"Enf. Smoke"}' | grep -q 'called'

VACCINE_LOTS=$(curl -fsS "$BASE_URL/api/immunization/lots")
VACCINE_LOT_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$VACCINE_LOTS")
curl -fsS -X POST "$BASE_URL/api/immunization/administer" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"vaccineLotId\":\"$VACCINE_LOT_ID\",\"dose\":\"Dose smoke\",\"route\":\"IM\",\"site\":\"Deltoide\",\"professional\":\"Enf. Smoke\",\"professionalCouncil\":\"COREN-SP DEMO\"}" | grep -q 'Dose smoke'

INVENTORY=$(curl -fsS "$BASE_URL/api/pharmacy/inventory")
INVENTORY_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$INVENTORY")
curl -fsS -X POST "$BASE_URL/api/pharmacy/dispense" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"inventoryLotId\":\"$INVENTORY_ID\",\"quantity\":1,\"prescriptionReference\":\"RX-SMOKE\",\"professional\":\"Farm. Smoke\"}" | grep -q 'dispense'

WAREHOUSE=$(curl -fsS "$BASE_URL/api/warehouse")
WAREHOUSE_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' <<<"$WAREHOUSE")
curl -fsS -X POST "$BASE_URL/api/warehouse/transfer" \
  -H 'Content-Type: application/json' \
  -d "{\"warehouseItemId\":\"$WAREHOUSE_ID\",\"quantity\":1,\"destinationUnitCode\":\"$UNIT_CODE\",\"reference\":\"REQ-SMOKE\",\"actor\":\"Warehouse Smoke\"}" | grep -q 'transfer'

curl -fsS -X POST "$BASE_URL/api/records/digitized" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"barcode\":null,\"documentType\":\"Prontuario smoke\",\"pages\":2,\"sourceUnit\":\"Smoke Unit\",\"storageReference\":\"BOX-SMOKE\",\"actor\":\"Arquivo Smoke\"}" | grep -q 'barcode'

curl -fsS -X POST "$BASE_URL/api/psf/esus/individuals" \
  -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"socialName\":null,\"raceColor\":\"Nao informado\",\"education\":\"Nao informado\",\"occupation\":\"Nao informado\",\"hasDisability\":false,\"isPregnant\":false,\"isBedridden\":false,\"chronicConditions\":[\"Hipertensao\"],\"acsName\":\"ACS Smoke\"}" | grep -q 'ACS Smoke'

curl -fsS "$BASE_URL/api/dashboard" | grep -q 'citizens'
curl -fsS "$BASE_URL/api/audit" | grep -q 'warehouse.transfer'

echo "Smoke POC completo OK"
