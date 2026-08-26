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

curl -fsS "$BASE_URL/api/health" >/dev/null || { echo "Aplicação wave4 não iniciou"; cat "$LOG_FILE"; exit 1; }
assert_json(){ local expression="$1"; python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"; }

for page in /immunization-v2.html /pharmacy-care.html /command-center.html /verification.html /workforce.html /referrals.html; do
  curl -fsS "$BASE_URL$page" >/dev/null || { echo "Página indisponível: $page"; exit 1; }
done

LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="mfa_required", d; print(d["challengeId"])' <<<"$LOGIN")
MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE_ID\",\"code\":\"008026\"}")
TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="authenticated", d; print(d["sessionToken"])' <<<"$MFA")
AUTH=(-H "Authorization: Bearer $TOKEN")

# A jornada inteira usa o MESMO cidadão de uma ordem clínica ativa real.
ORDERS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/clinical/orders")
ORDER_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); m=[x for x in d if x.get("status")=="active"]; assert m, "Nenhuma ordem clínica ativa no seed"; print(m[0]["id"])' <<<"$ORDERS")
CITIZEN_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; m=[x for x in d if x.get("id")==oid]; assert m, "Ordem selecionada desapareceu"; print(m[0]["citizenId"])' "$ORDER_ID" <<<"$ORDERS")
ORDER_MED=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; m=[x for x in d if x.get("id")==oid]; assert m; print(m[0]["medication"])' "$ORDER_ID" <<<"$ORDERS")
ORDER_DOSE=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; m=[x for x in d if x.get("id")==oid]; assert m; print(m[0]["dose"])' "$ORDER_ID" <<<"$ORDERS")
ORDER_FREQ=$(python3 -c 'import json,sys; d=json.load(sys.stdin); oid=sys.argv[1]; m=[x for x in d if x.get("id")==oid]; assert m; print(m[0]["frequency"])' "$ORDER_ID" <<<"$ORDERS")

CITIZENS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/citizens")
CITIZEN_NAME=$(python3 -c 'import json,sys; d=json.load(sys.stdin); cid=sys.argv[1]; m=[x for x in d if x.get("id")==cid]; assert m, f"Cidadão {cid} da ordem não existe no DemoStore"; print(m[0]["name"])' "$CITIZEN_ID" <<<"$CITIZENS")
CITIZEN_UNIT=$(python3 -c 'import json,sys; d=json.load(sys.stdin); cid=sys.argv[1]; m=[x for x in d if x.get("id")==cid]; assert m; print(m[0]["healthUnit"])' "$CITIZEN_ID" <<<"$CITIZENS")

# Rede profissional + alertas de credencial.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/professionals/readiness" | assert_json 'd["professionals"]>=8 and d["assignments"]>=8'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/professionals/credential-alerts" | assert_json 'len(d)>=1'

# Referência do mesmo cidadão -> aceite -> contrarreferência. Se não houver seed, cria uma referência real.
REFS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/referrals?citizenId=$CITIZEN_ID")
REF_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); m=[x for x in d if x.get("status")=="requested"]; print(m[0]["id"] if m else "")' <<<"$REFS")
if [[ -z "$REF_ID" ]]; then
  REF_BODY=$(python3 -c 'import json,sys; print(json.dumps({"citizenId":sys.argv[1],"originUnit":sys.argv[2],"destinationService":"Cardiologia","priority":"high","clinicalQuestion":"Avaliação especializada da jornada smoke.","requestedBy":"Dr. Smoke APS","professionalCouncil":"CRM-SP DEMO"}))' "$CITIZEN_ID" "$CITIZEN_UNIT")
  CREATED_REF=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/referrals" -H 'Content-Type: application/json' -d "$REF_BODY")
  REF_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("citizenId") and d.get("status")=="requested", d; print(d["id"])' <<<"$CREATED_REF")
fi
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/referrals/$REF_ID/accept" -H 'Content-Type: application/json' \
  -d '{"actor":"Regulador Smoke","note":"Aceite do smoke"}' | assert_json 'd["status"]=="accepted"'
COUNTER_BODY=$(python3 -c 'import json,sys; print(json.dumps({"assessment":"Avaliação especializada smoke concluída.","plan":"Retorno e seguimento pela APS.","returnToUnit":sys.argv[1],"followUpAt":None,"professional":"Dr. Smoke"}))' "$CITIZEN_UNIT")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/referrals/$REF_ID/counter-referral" -H 'Content-Type: application/json' -d "$COUNTER_BODY" | assert_json 'd["status"]=="counter_referred" and d["returnToUnit"]'

# Imunização avançada: seleciona explicitamente regra Influenza e lote Influenza válido.
IMM_READY=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/v2/readiness")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["scheduleRules"]>=4, d' <<<"$IMM_READY"
SCHEDULE=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/v2/schedule?citizenId=$CITIZEN_ID")
RULE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d)==1 and d[0]["items"], d; m=[x for x in d[0]["items"] if x.get("ruleId")=="DEMO-INFLUENZA-ANNUAL"]; assert m, "Regra Influenza não disponível para cidadão da jornada"; print(m[0]["ruleId"])' <<<"$SCHEDULE")
SCREEN=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/screen" -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"ruleId\":\"$RULE_ID\",\"severeAllergicReactionToPreviousDose\":false,\"acuteFebrileIllness\":false,\"pregnant\":false,\"immunosuppressed\":false,\"actor\":\"Enf. Smoke\"}")
SCREEN_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["disposition"]=="eligible", d; print(d["id"])' <<<"$SCREEN")
LOTS=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/immunization/lots")
VACCINE_LOT_ID=$(python3 -c 'import json,sys,datetime; d=json.load(sys.stdin); today=datetime.date.today(); m=[x for x in d if x.get("stock",0)>0 and x.get("vaccine","").lower().startswith("influenza") and datetime.date.fromisoformat(x["expiresOn"])>=today]; assert m, "Sem lote Influenza válido no seed"; print(m[0]["id"])' <<<"$LOTS")
IMM=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/administer" -H 'Content-Type: application/json' \
  -d "{\"screeningId\":\"$SCREEN_ID\",\"citizenId\":\"$CITIZEN_ID\",\"vaccineLotId\":\"$VACCINE_LOT_ID\",\"route\":\"IM\",\"site\":\"Deltoide\",\"professional\":\"Enf. Smoke\",\"professionalCouncil\":\"COREN-SP DEMO\",\"clinicalReviewApproved\":false}")
IMM_EVENT_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d.get("event",{}).get("id"), d; print(d["event"]["id"])' <<<"$IMM")
ADVERSE=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/adverse-events" -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"immunizationEventId\":\"$IMM_EVENT_ID\",\"vaccine\":\"Influenza demonstrativa\",\"lot\":\"smoke\",\"severity\":\"mild\",\"description\":\"Dor local demonstrativa\",\"startedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"reportedBy\":\"Enf. Smoke\"}")
ADVERSE_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="open", d; print(d["id"])' <<<"$ADVERSE")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/immunization/v2/adverse-events/$ADVERSE_ID/review" -H 'Content-Type: application/json' \
  -d '{"status":"closed","reviewer":"Enf. Revisora Smoke","assessment":"Evento leve, encerrado na demonstração."}' | assert_json 'd["status"]=="closed"'

# Farmácia clínica: conciliação -> dispensação vinculada à mesma ordem -> orientação.
RECONCILE_BODY=$(python3 -c 'import json,sys; print(json.dumps({"citizenId":sys.argv[1],"reportedMedications":[{"name":sys.argv[2],"dose":sys.argv[3],"frequency":sys.argv[4],"source":"patient_report"}],"pharmacist":"Farm. Smoke","professionalCouncil":"CRF-SP DEMO","context":"ambulatory"}))' "$CITIZEN_ID" "$ORDER_MED" "$ORDER_DOSE" "$ORDER_FREQ")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/v2/reconciliations" -H 'Content-Type: application/json' -d "$RECONCILE_BODY" | assert_json 'd["status"] in ("reconciled","review_required") and d["citizenId"]'
INV=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/pharmacy/inventory")
INV_ID=$(python3 -c 'import json,sys,datetime; d=json.load(sys.stdin); today=datetime.date.today(); m=[x for x in d if x.get("quantity",0)>0 and datetime.date.fromisoformat(x["expiresOn"])>=today]; assert m, "Sem lote de farmácia válido"; print(m[0]["id"])' <<<"$INV")
DISP=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/v2/dispense" -H 'Content-Type: application/json' \
  -d "{\"citizenId\":\"$CITIZEN_ID\",\"clinicalOrderId\":\"$ORDER_ID\",\"inventoryLotId\":\"$INV_ID\",\"quantity\":1,\"prescriptionReference\":\"RX-WAVE4-SMOKE\",\"pharmacist\":\"Farm. Smoke\",\"professionalCouncil\":\"CRF-SP DEMO\"}")
DISP_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="dispensed" and d["clinicalOrderId"], d; print(d["id"])' <<<"$DISP")
curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/pharmacy/v2/dispensations/$DISP_ID/counsel" -H 'Content-Type: application/json' \
  -d '{"pharmacist":"Farm. Smoke","note":"Orientação farmacêutica demonstrativa registrada."}' | assert_json 'd["counselingNote"] is not None'

# Command Center cruza contextos e deve refletir a jornada criada acima.
COMMAND=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/analytics/command-center")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["network"]["healthUnits"]==58, d; assert d["network"]["professionals"]>=8, d; assert "referralsOpen" in d["access"], d; assert d["prevention"]["scheduleRules"]>=4, d; assert d["supply"]["linkedDispensations"]>=1, d' <<<"$COMMAND"

# Runner dos 14 blocos grava evidência e deve passar integralmente.
VERIFY=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/verification/run" -H 'Content-Type: application/json' -d '{}')
python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["totalBlocks"]==14, d; assert d["passedBlocks"]==14, d; assert d["status"]=="passed", d; assert d["overallScore"]>=90, d' <<<"$VERIFY"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/readiness" | assert_json 'd["contract"]["overallScore"]>=90 and d["workforce"]["professionals"]>=8 and d["verification"]["passedBlocks"]==14'

# CareTrace do mesmo cidadão deve enxergar referência/contrarreferência e ordem clínica.
TRACE=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/care-trace/$CITIZEN_ID")
python3 -c 'import json,sys; d=json.load(sys.stdin); assert any(n["type"]=="referral" for n in d["nodes"]), d; assert any(n["type"]=="clinical_order" for n in d["nodes"]), d; assert len(d["edges"])>=2, d' <<<"$TRACE"

curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/verify" | assert_json 'd["valid"] is True and d["checkedEvents"]>=15'

echo "Smoke wave 4 OK · cidadão=$CITIZEN_NAME · id=$CITIZEN_ID"
