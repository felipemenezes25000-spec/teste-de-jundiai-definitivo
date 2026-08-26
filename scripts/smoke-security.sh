#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5111"
HARDENED_URL="http://127.0.0.1:5112"
LOG_FILE="/tmp/jundiai-healthos-security.log"
HARDENED_LOG="/tmp/jundiai-healthos-security-hardened.log"
HARDENED_PID=""

dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$BASE_URL" >"$LOG_FILE" 2>&1 &
APP_PID=$!
cleanup(){
  kill "$APP_PID" >/dev/null 2>&1 || true
  if [[ -n "$HARDENED_PID" ]]; then kill "$HARDENED_PID" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT

for _ in $(seq 1 35); do
  if curl -fsS "$BASE_URL/api/health/live" >/dev/null 2>&1; then break; fi
  sleep 1
done

assert_json(){ local expression="$1"; python3 -c "import json,sys; d=json.load(sys.stdin); assert ($expression), d"; }

# 0. Headers defensivos já implementados devem aparecer nas respostas da instância.
HEADERS=$(mktemp)
curl -fsS -D "$HEADERS" -o /dev/null "$BASE_URL/api/health/live"
grep -Eqi '^X-Jundiai-POC:[[:space:]]*RCE-008-2026' "$HEADERS"
grep -Eqi '^X-Content-Type-Options:[[:space:]]*nosniff' "$HEADERS"
grep -Eqi '^X-Frame-Options:[[:space:]]*SAMEORIGIN' "$HEADERS"
grep -Eqi '^Referrer-Policy:[[:space:]]*same-origin' "$HEADERS"
grep -Eqi '^Permissions-Policy:[[:space:]]*camera=\(self\),[[:space:]]*microphone=\(self\),[[:space:]]*geolocation=\(self\)' "$HEADERS"

# 1. Rota protegida sem sessão deve falhar fechada com 401.
CODE=$(curl -sS -o /tmp/anonymous.json -w '%{http_code}' "$BASE_URL/api/sus/production")
[[ "$CODE" == "401" ]] || { echo "Expected 401 for anonymous protected API, got $CODE"; cat /tmp/anonymous.json; exit 1; }
cat /tmp/anonymous.json | assert_json 'd["status"]==401 and d["role"]=="anonymous"'

# 2. Header de papel não pode conceder acesso por padrão.
CODE=$(curl -sS -o /tmp/forged-role.json -w '%{http_code}' -H 'X-Demo-Role: poc_admin' "$BASE_URL/api/sus/production")
[[ "$CODE" == "403" ]] || { echo "Expected 403 for disabled X-Demo-Role, got $CODE"; cat /tmp/forged-role.json; exit 1; }
cat /tmp/forged-role.json | assert_json 'd["status"]==403 and d["role"].startswith("blocked_demo_header:")'

# 3. Login real da ACS: pode acessar PSF, mas não faturamento/auditoria/contrato.
ACS_LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"acs.micro01","password":"Acs#008"}')
ACS_TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="authenticated" and d["role"]=="acs"; print(d["sessionToken"])' <<<"$ACS_LOGIN")
ACS_AUTH=(-H "Authorization: Bearer $ACS_TOKEN")

curl -fsS "${ACS_AUTH[@]}" "$BASE_URL/api/auth/me" | assert_json 'd["userName"]=="acs.micro01" and d["role"]=="acs"'
curl -fsS "${ACS_AUTH[@]}" "$BASE_URL/api/psf/esus/individuals" | assert_json 'isinstance(d,list)'
curl -fsS "${ACS_AUTH[@]}" "$BASE_URL/api/access/context" | assert_json 'd["authenticated"] is True and d["role"]=="acs" and "psf.read" in d["permissions"] and "billing.read" not in d["permissions"] and d["demoRoleHeaderEnabled"] is False'

for route in /api/sus/production /api/audit /api/contract/jundiai/readiness; do
  CODE=$(curl -sS -o /tmp/acs-denied.json -w '%{http_code}' "${ACS_AUTH[@]}" "$BASE_URL$route")
  [[ "$CODE" == "403" ]] || { echo "Expected ACS 403 at $route, got $CODE"; cat /tmp/acs-denied.json; exit 1; }
done

# 4. Médico real: acesso clínico permitido, faturamento negado.
MED_LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"medico.ubs","password":"Medico#008"}')
MED_TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="authenticated" and d["role"]=="clinician"; print(d["sessionToken"])' <<<"$MED_LOGIN")
MED_AUTH=(-H "Authorization: Bearer $MED_TOKEN")
curl -fsS "${MED_AUTH[@]}" "$BASE_URL/api/clinical/workspaces" >/dev/null
CODE=$(curl -sS -o /tmp/clinician-billing.json -w '%{http_code}' "${MED_AUTH[@]}" "$BASE_URL/api/sus/production")
[[ "$CODE" == "403" ]] || { echo "Expected clinician 403 for billing, got $CODE"; cat /tmp/clinician-billing.json; exit 1; }

# 5. Administrador via MFA continua com acesso e readiness explicita hardening.
ADMIN_LOGIN=$(curl -fsS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
CHALLENGE=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="mfa_required"; print(d["challengeId"])' <<<"$ADMIN_LOGIN")
ADMIN_MFA=$(curl -fsS -X POST "$BASE_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$CHALLENGE\",\"code\":\"008026\"}")
ADMIN_TOKEN=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="authenticated" and d["mfaVerified"] is True; print(d["sessionToken"])' <<<"$ADMIN_MFA")
ADMIN_AUTH=(-H "Authorization: Bearer $ADMIN_TOKEN")
curl -fsS "${ADMIN_AUTH[@]}" "$BASE_URL/api/security/readiness" | assert_json 'd["pocMode"] is True and d["mfaDefaultCodeEnabled"] is True and d["mfaCodeSource"]=="explicit-poc-default" and d["anonymousProtectedApi"]=="401-fail-closed" and d["demoRoleHeaderEnabled"] is False and d["responseHeaders"]["contentTypeOptions"]=="nosniff" and d["responseHeaders"]["frameOptions"]=="SAMEORIGIN"'
curl -fsS "${ADMIN_AUTH[@]}" "$BASE_URL/api/sus/production" >/dev/null

# 6. Logout revoga sessão imediatamente.
curl -fsS -X POST "${ACS_AUTH[@]}" "$BASE_URL/api/auth/logout" >/dev/null
CODE=$(curl -sS -o /tmp/revoked.json -w '%{http_code}' "${ACS_AUTH[@]}" "$BASE_URL/api/psf/esus/individuals")
[[ "$CODE" == "401" ]] || { echo "Expected revoked ACS session to return 401, got $CODE"; cat /tmp/revoked.json; exit 1; }

# 7. Quando o default demonstrativo é desligado e não há código no ambiente, MFA deve falhar fechado.
env -u JUNDIAI_DEMO_MFA_CODE Jundiai__DemoMfa__AllowDefaultCode=false \
  dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj -c Release --no-build --urls "$HARDENED_URL" >"$HARDENED_LOG" 2>&1 &
HARDENED_PID=$!
for _ in $(seq 1 35); do
  if curl -fsS "$HARDENED_URL/api/health/live" >/dev/null 2>&1; then break; fi
  sleep 1
done
HARDENED_LOGIN=$(curl -fsS -X POST "$HARDENED_URL/api/auth/login" -H 'Content-Type: application/json' -d '{"userName":"admin.jundiai","password":"Jundiai#008"}')
HARDENED_CHALLENGE=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["status"]=="mfa_required"; print(d["challengeId"])' <<<"$HARDENED_LOGIN")
CODE=$(curl -sS -o /tmp/hardened-mfa.json -w '%{http_code}' -X POST "$HARDENED_URL/api/auth/mfa/verify" -H 'Content-Type: application/json' -d "{\"challengeId\":\"$HARDENED_CHALLENGE\",\"code\":\"008026\"}")
[[ "$CODE" == "401" ]] || { echo "Expected default MFA code to fail when fallback is disabled, got $CODE"; cat /tmp/hardened-mfa.json; exit 1; }

echo "Smoke segurança negativa OK: headers, anonymous 401, forged-role 403, RBAC real, MFA configurável/fail-closed e revogacao"
