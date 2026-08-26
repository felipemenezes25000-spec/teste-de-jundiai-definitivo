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
PRESENTATION_ID=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["ready"] is True and d["status"]=="ready"; assert d["passedBlocks"]==14 and d["totalBlocks"]==14; assert len(d["checks"])==8 and all(x["passed"] for x in d["checks"]); assert len(d["pages"])==24 and all(x["exists"] and x["bytes"]>0 for x in d["pages"]); assert len(d["assets"])==12 and all(x["exists"] and x["bytes"]>0 for x in d["assets"]); assert len(d["evidencePackSha256"])==64; assert d["persistenceMode"]=="poc-memory-fallback"; assert any(x["id"]=="HAB-AT-29" for x in d["nonCodeBlockers"]); print(d["id"])' <<<"$PRESENTATION")
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/presentation/latest" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["id"]==sys.argv[1] and d["ready"] is True and d["passedBlocks"]==14' "$PRESENTATION_ID"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/presentation/checklist" | assert_json 'len(d["pages"])==24 and all(x["exists"] for x in d["pages"]) and len(d["assets"])==12 and all(x["exists"] for x in d["assets"]) and "Kit de Contingência" in d["presentationOrder"]'

# Identidade do build: honesta quando a revisão não foi injetada; vinculada ao commit quando CI/deploy fornece GITHUB_SHA/JUNDIAI_BUILD_SHA.
BUILD=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/platform/build-identity")
python3 -c 'import json,sys,os; d=json.load(sys.stdin); assert d["service"]=="Jundiai HealthOS" and d["contract"]=="RCE 008/2026"; assert d["repository"].endswith("teste-de-jundiai-definitivo"); assert d["runtime"] and d["runtimeIdentifier"]; expected=os.environ.get("JUNDIAI_BUILD_SHA") or os.environ.get("GITHUB_SHA"); assert (not expected) or d["sourceRevision"]==expected' <<<"$BUILD"

# Inventário POC deve estar embarcado, hasheado e continuar explicitamente diferente de SBOM formal.
INVENTORY=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/platform/dependency-inventory")
python3 -c 'import json,sys; d=json.load(sys.stdin); s=d["summary"]; assert s["exists"] is True and len(s["sha256"])==64; assert s["formalSbom"] is False; assert s["dotnetDirectDependencies"]==2 and s["npmDirectDependencies"]==1 and s["containerImages"]==3; assert s["npmLockfile"]=="absent"; assert d["inventory"]["formalSbom"] is False' <<<"$INVENTORY"

# Provenance dos bytes realmente carregados pela instância.
RELEASE=$(curl -fsS "${AUTH[@]}" "$BASE_URL/api/platform/release-provenance")
python3 -c 'import json,sys,os; d=json.load(sys.stdin); p=d["payload"]; assert len(d["manifestSha256"])==64; assert p["runtimeArtifactsComplete"] is True; assert len(p["files"])==4 and all(x["exists"] and x["bytes"]>0 and len(x["sha256"])==64 for x in p["files"]); assert any(x["name"]=="supply-chain.inventory.json" for x in p["files"]); assert p["dependencyInventory"]["exists"] is True and p["dependencyInventory"]["formalSbom"] is False and len(p["dependencyInventory"]["sha256"])==64; assert len(p["runtimeLibraries"])>0 and len(p["runtimeLibrariesSha256"])==64; expected=os.environ.get("JUNDIAI_BUILD_SHA") or os.environ.get("GITHUB_SHA"); assert (not expected) or p["build"]["sourceRevision"]==expected' <<<"$RELEASE"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/platform/release-provenance/verify" | assert_json 'd["integrityReady"] is True and d["manifestHashValid"] is True and d["runtimeFilesValid"] is True and len(d["files"])==4 and all(x["valid"] for x in d["files"])'

# Dossiê final: congela preflight + Evidence Pack + build + provenance em payload canônico verificável.
DOSSIER=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/dossier" -H 'Content-Type: application/json' -d '{"actor":"smoke.dossier","refreshPreflight":false}')
DOSSIER_CODE=$(python3 -c 'import json,sys,re,os; d=json.load(sys.stdin); assert re.fullmatch(r"JUN-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}", d["verificationCode"]); assert len(d["dossierSha256"])==64; p=d["payload"]; assert p["preflight"]["ready"] is True and p["preflight"]["passedBlocks"]==14 and p["preflight"]["totalBlocks"]==14; assert len(p["evidencePack"]["packageSha256"])==64; assert p["build"]["service"]=="Jundiai HealthOS"; assert len(p["release"]["manifestSha256"])==64 and p["release"]["payload"]["runtimeArtifactsComplete"] is True; assert len(p["release"]["payload"]["files"])==4 and all(x["exists"] for x in p["release"]["payload"]["files"]); assert p["release"]["payload"]["dependencyInventory"]["formalSbom"] is False; assert any(x["id"]=="HAB-AT-29" for x in p["preflight"]["nonCodeBlockers"]); expected=os.environ.get("JUNDIAI_BUILD_SHA") or os.environ.get("GITHUB_SHA"); assert (not expected) or p["build"]["sourceRevision"]==expected; print(d["verificationCode"])' <<<"$DOSSIER")
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/dossier/$DOSSIER_CODE/verify" | assert_json 'd["integrityReady"] is True and d["dossierHashValid"] is True and d["verificationCodeValid"] is True and d["evidencePackHashValid"] is True and d["evidenceLedgerValid"] is True and d["releaseManifestHashValid"] is True and d["runtimeFilesValid"] is True and d["preflightReady"] is True and d["passedBlocks"]==14 and d["totalBlocks"]==14'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/dossier/$DOSSIER_CODE/export" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["verificationCode"]==sys.argv[1] and len(d["dossierSha256"])==64 and len(d["payload"]["release"]["manifestSha256"])==64' "$DOSSIER_CODE"
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/dossiers" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert len(d)>=1 and any(x["verificationCode"]==sys.argv[1] and len(x["releaseManifestSha256"])==64 for x in d)' "$DOSSIER_CODE"

# Kit de contingência: ZIP estático com manifesto, hashes e HTML autocontido.
KIT=$(curl -fsS -X POST "${AUTH[@]}" "$BASE_URL/api/poc/contingency-bundle" -H 'Content-Type: application/json' -d '{"actor":"smoke.contingency","refreshDossier":false}')
KIT_CODE=$(python3 -c 'import json,sys,re,os; d=json.load(sys.stdin); assert re.fullmatch(r"KIT-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}", d["verificationCode"]); assert len(d["manifestSha256"])==64 and len(d["zipSha256"])==64; assert d["zipBytes"]>0 and d["fileCount"]==6; assert d["dossierVerificationCode"].startswith("JUN-"); expected=os.environ.get("JUNDIAI_BUILD_SHA") or os.environ.get("GITHUB_SHA"); assert (not expected) or d["sourceRevision"]==expected; print(d["verificationCode"])' <<<"$KIT")
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/contingency-bundle/$KIT_CODE/verify" | assert_json 'd["integrityReady"] is True and d["manifestHashValid"] is True and d["verificationCodeValid"] is True and d["entriesValid"] is True and d["zipHashValid"] is True and d["dossierIntegrityReady"] is True and len(d["files"])==5 and all(x["valid"] for x in d["files"])'
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/contingency-bundle/$KIT_CODE/download" -o /tmp/jundiai-contingency.zip
python3 - <<'PY'
import json, zipfile, hashlib
path='/tmp/jundiai-contingency.zip'
with zipfile.ZipFile(path) as z:
    names=set(z.namelist())
    expected={'dossier.json','evidence-pack.json','release-provenance.json','verification.txt','presentation-summary.html','manifest.json'}
    assert names==expected, names
    html=z.read('presentation-summary.html').decode('utf-8')
    assert '<!doctype html>' in html.lower()
    assert '14 blocos' in html
    assert 'http://' not in html.lower() and 'https://' not in html.lower()
    manifest=json.loads(z.read('manifest.json'))
    assert len(manifest['manifestSha256'])==64
    assert len(manifest['payload']['files'])==5
    release=json.loads(z.read('release-provenance.json'))
    assert len(release['payload']['files'])==4
    assert release['payload']['dependencyInventory']['formalSbom'] is False
    for item in manifest['payload']['files']:
        body=z.read(item['name'])
        assert len(body)==item['bytes']
        assert hashlib.sha256(body).hexdigest()==item['sha256']
PY
curl -fsS "${AUTH[@]}" "$BASE_URL/api/poc/contingency-bundles" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert any(x["verificationCode"]==sys.argv[1] and x["fileCount"]==6 for x in d)' "$KIT_CODE"

# Evidence Ledger recebeu controles de privacidade, runner, pacote, preflight, dossiê e contingência.
curl -fsS "${AUTH[@]}" "$BASE_URL/api/evidence/ledger" | assert_json 'any(x["action"]=="privacy.break-glass.open" for x in d) and any(x["action"]=="privacy.subject-export.generate" for x in d) and any(x["action"]=="poc.run.complete" for x in d) and any(x["action"]=="poc.evidence-pack.generate" for x in d) and any(x["action"]=="poc.presentation.prepare" for x in d) and any(x["action"]=="poc.dossier.generate" for x in d) and any(x["action"]=="poc.contingency.generate" for x in d)'

echo "Smoke plataforma + Evidence Pack + READY 8/8 + inventario + provenance + Dossiê + contingência OK"
