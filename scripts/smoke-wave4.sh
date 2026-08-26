#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5102"
LOG_FILE="/tmp/jundiai-healthos-wave4.log"

dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" >/dev/null 2>&1 || true' EXIT

for _ in $(seq 1 40); do
  if curl -fsS "$BASE_URL/api/health" >/dev/null 2>&1; then break; fi
  sleep 1
done

assert_json(){ local expression="$1"; python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"; }

for page in /immunization-v2.html /pharmacy-care.html /command-center.html /verification.html /workforce.html /referrals.html; do
  curl -fsS "$BASE_URL$page" >/dev/null
done

LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="mfa_required"; print(d["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="authenticated"; print(d["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN")

CITIZENS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/citizens")
CITIZEN_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); preferred="11111111-1111-1111-1111-111111111111"; print(next((x["id"] for x in d if x["id"]==preferred),d[0]["id"]))' <<<"$CITIZENS")
CITIZEN_NAME=$(python3 -c 'import json,sys; d=json.load(sys.stdin); cid=sys.argv[1]; print(next(x["name"] for x in d if x["id"]==cid))' "$CITIZEN_ID" <<<"$CITIZENS")

# Rede profissional + alertas de credencial
curl -fsS "${AUTH[@]}" "$BASE_URL/api/professionals/readiness" | assert_json 'd["professionals"]>=8 and d["assignments"]>=8'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/professionals/credential-alerts" | assert_json 'len(d)>=1'

# Referência -> aceite -> contrarreferência
REFS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/referrals")
REF_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); print(next(x["id"] for x in d if x["status"]=="requested"))' <<<"$REFS")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/referrals/$REF_ID/accept" -H 'Content-Type: application/json' \
  -d '{"actor":"Regulador Smoke","note":"Aceite do smoke"}' | assert_json 'd["status"]=="accepted"'
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/referrals/$REF_ID/counter-referral" -H 'Content-Type: application/json' \
  -d '{"assessment":"Avaliação especializada smoke concluída.","plan":"Retorno e seguimento pela APS.","returnToUnit":"UBS Vila Hortolândia","followUpAt":null,"professional":"Dr. Smoke"}' | assert_json 'd["status"]=="counter_referred" and d["returnToUnit"]=="UBS Vila Hortolândia"'

# Imunização avançada: calendário -> screening -> aplicação -> evento adverso -> revisão
IMM_READY=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/v2/readiness")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["scheduleRules"]>=4' <<<"$IMM_READY"
SCHEDULE=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/v2/schedule?citizenId=$CITIZEN_ID")
RULE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d)==1 and len(d[0]["items"])>0; print(d[0]["items"][0]["ruleId"])' <<<"$SCHEDULE")
SCREEN=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/screen" -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"ruleId\":\"$RULE_ID\",\"severeAllergicReactionToPreviousDose\":false,\"acuteFebrileIllness\":false,\"pregnant\":false,\"immunosuppressed\":false,\"actor\":\"Enf. Smoke\"}")
SCREEN_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["disposition"]=="eligible"; print(d["id"])' <<<"$SCREEN")
LOTS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/lots")
VACCINE_LOT_ID=$(python3 -c 'import json,sys,datetime; d=json.load(sys.stdin); today=datetime.date.today(); x=next(x for x in d if x["stock"]>0 and datetime.date.fromisoformat(x["expiresOn"])>=today); print(x["id"])' <<<"$LOTS")
IMM=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/administer" -H 'Content-Type: application/json' \
  -d "{\"screeningId\":\"$SCREEN_ID\",\"citizenId\":\"$CITIZEN_ID\",\"vaccineLotId\":\"$VACCINE_LOT_ID\",\"route\":\"IM\",\"site\":\"Deltoide\",\"professional\":\"Enf. Smoke\",\"professionalCouncil\":\"COREN-SP DEMO\",\"clinicalReviewApproved\":false}")
IMM_EVENT_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["event"]["id"]; print(d["event"]["id"])' <<<"$IMM")
ADVERSE=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/adverse-events" -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"immunizationEventId\":\"$IMM_EVENT_ID\",\"vaccine\":\"demonstrativa\",\"lot\":\"smoke\",\"severity\":\"mild\",\"description\":\"Dor local demonstrativa\",\"startedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"reportedBy\":\"Enf. Smoke\"}")
ADVERSE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="open"; print(d["id"])' <<<"$ADVERSE")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/adverse-events/$ADVERSE_ID/review" -H 'Content-Type: application/json' \
  -d '{"status":"closed","reviewer":"Enf. Revisora Smoke","assessment":"Evento leve, encerrado na demonstração."}' | assert_json 'd["status"]=="closed"'

# Farmácia clínica: conciliação -> dispensação vinculada à ordem -> orientação
ORDERS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/clinical/orders?citizenId=$CITIZEN_ID")
ORDER_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); x=next(x for x in d if x["status"]=="active"); print(x["id"])' <<<"$ORDERS")
ORDER_MED=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; print(next(x["medication"] for x in d if x["id"]==oid))' "$ORDER_ID" <<<"$ORDERS")
ORDER_DOSE=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; print(next(x["dose"] for x in d if x["id"]==oid))' "$ORDER_ID" <<<"$ORDERS")
ORDER_FREQ=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; print(next(x["frequency"] for x in d if x["id"]==oid))' "$ORDER_ID" <<<"$ORDERS")
RECONCILE_BODY=$(python3 -c 'import json,sys; print(json.dumps({"citizenId":sys.argv[1],"reportedMedications":[{"name":sys.argv[2],"dose":sys.argv[3],"frequency":sys.argv[4],"source":"patient_report"}],"pharmacist":"Farm. Smoke","professionalCouncil":"CRF-SP DEMO","context":"ambulatory"}))' "$CITIZEN_ID" "$ORDER_MED" "$ORDER_DOSE" "$ORDER_FREQ")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/v2/reconciliations" -H 'Content-Type: application/json' -d "$RECONCILE_BODY" | assert_json 'd["status"] in ("reconciled","review_required")'
INV=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/pharmacy/inventory")
INV_ID=$(python3 -c 'import json,sys,datetime; d=json.load(sys.stdin); today=datetime.date.today(); x=next(x for x in d if x["quantity"]>0 and datetime.date.fromisoformat(x["expiresOn"])>=today); print(x["id"])' <<<"$INV")
DISP=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/v2/dispense" -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"clinicalOrderId\":\"$ORDER_ID\",\"inventoryLotId\":\"$INV_ID\",\"quantity\":1,\"prescriptionReference\":\"RX-WAVE4-SMOKE\",\"pharmacist\":\"Farm. Smoke\",\"professionalCouncil\":\"CRF-SP DEMO\"}")
DISP_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="dispensed" and d["clinicalOrderId"]; print(d["id"])' <<<"$DISP")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/v2/dispensations/$DISP_ID/counsel" -H 'Content-Type: application/json' \
  -d '{"pharmacist":"Farm. Smoke","note":"Orientação farmacêutica demonstrativa registrada."}' | assert_json 'd["counselingNote"] is not None'

# Command Center deve cruzar os novos contextos
COMMAND=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/analytics/command-center")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["network"]["healthUnits"]==58; assert d["network"]["professionals"]>=8; assert "referralsOpen" in d["access"]; assert d["prevention"]["scheduleRules"]>=4; assert d["supply"]["linkedDispensations"]>=1' <<<"$COMMAND"

# Runner dos 14 blocos grava evidência e deve passar integralmente sobre o seed funcional
VERIFY=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/verification/run" -H 'Content-Type: application/json' -d '{}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["totalBlocks"]==14; assert d["passedBlocks"]==14; assert d["status"]=="passed"; assert d["overallScore"]>=90' <<<"$VERIFY"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/readiness" | assert_json 'd["contract"]["overallScore"]>=90 and d["workforce"]["professionals"]>=8 and d["verification"]["passedBlocks"]==14'

# CareTrace deve enxergar referência/contrarreferência
TRACE=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/care-trace/$CITIZEN_ID")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert any(n["type"]=="referral" for n in d["nodes"]); assert len(d["edges"])>=1' <<<"$TRACE"

curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/verify" | assert_json 'd["valid"] is True and d["checkedEvents"]>=15'

echo "Smoke wave 4 OK"