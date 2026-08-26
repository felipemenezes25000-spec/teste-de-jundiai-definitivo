const q = s => document.querySelector(s);
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const session = () => localStorage.getItem('jundiai.session');
let current = null;

function authHeaders(extra = {}) {
  const token = session();
  return {
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...extra
  };
}

async function api(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: authHeaders({ 'Content-Type': 'application/json', ...(options.headers || {}) })
  });
  const type = response.headers.get('content-type') || '';
  const body = type.includes('application/json') ? await response.json().catch(() => ({})) : await response.text();
  if (!response.ok) throw new Error(body?.detail || body?.title || `HTTP ${response.status}`);
  return body;
}

function showError(error) {
  q('#error').hidden = false;
  q('#error').textContent = error?.message || String(error);
}

function clearError() {
  q('#error').hidden = true;
  q('#error').textContent = '';
}

function metric(label, value, sub) {
  return `<article><small>${esc(label)}</small><strong>${esc(value)}</strong><small>${esc(sub)}</small></article>`;
}

function render(artifact) {
  current = artifact;
  const payload = artifact.payload;
  const preflight = payload.preflight;
  const pack = payload.evidencePack;
  const build = payload.build;
  const release = payload.release;
  const inventory = release.payload.dependencyInventory || {};
  const blocks = pack.payload.blocks || [];
  const blockers = preflight.nonCodeBlockers || pack.payload.nonCodeBlockers || [];

  q('#verification-code').textContent = artifact.verificationCode;
  const status = q('#dossier-status');
  status.className = `status-card ${preflight.ready ? 'ready' : 'attention'}`;
  status.innerHTML = `<small>estado</small><strong>${preflight.ready ? 'READY' : 'ATENÇÃO'}</strong><span>${preflight.passedBlocks}/${preflight.totalBlocks} blocos · ${preflight.checks.filter(x => x.passed).length}/${preflight.checks.length} checks</span>`;

  q('#summary').innerHTML = [
    ['Blocos', `${preflight.passedBlocks}/${preflight.totalBlocks}`, 'runner funcional'],
    ['Checks', `${preflight.checks.filter(x => x.passed).length}/${preflight.checks.length}`, 'preflight da banca'],
    ['Score', `${preflight.overallScore}%`, 'readiness POC'],
    ['Runtime', `${release.payload.files.filter(x => x.exists).length}/${release.payload.files.length}`, 'artefatos hasheados'],
    ['Libraries', release.payload.runtimeLibraries.length, 'extraídas do .deps.json'],
    ['Inventário', inventory.exists ? 'HASHED' : 'AUSENTE', inventory.formalSbom ? 'SBOM formal' : 'POC · não é SBOM'],
    ['Blockers', blockers.length, 'não resolvidos por código'],
    ['Build', build.sourceRevision ? build.sourceRevision.slice(0, 12) : 'não injetado', build.sourceRevisionInjected ? 'revisão do processo' : 'defina JUNDIAI_BUILD_SHA']
  ].map(x => metric(...x)).join('');

  q('#build-proof').innerHTML = `<h3>Build + runtime</h3><div class="rows">
    <div class="row"><span>repositório</span><strong>${esc(build.repository)}</strong></div>
    <div class="row"><span>revisão</span><strong>${esc(build.sourceRevision || 'não injetada')}</strong></div>
    <div class="row"><span>run validação</span><strong>${esc(build.validationRunId || 'não informado')}</strong></div>
    <div class="row"><span>runtime</span><strong>${esc(build.runtime)}</strong></div>
    <div class="row"><span>RID</span><strong>${esc(build.runtimeIdentifier)}</strong></div>
    <div class="row"><span>artefatos</span><strong>${release.payload.files.filter(x => x.exists).length}/${release.payload.files.length}</strong></div>
    <div class="row"><span>libraries runtime</span><strong>${release.payload.runtimeLibraries.length}</strong></div>
    <div class="row"><span>inventário POC</span><strong>${inventory.exists ? 'embarcado' : 'ausente'} · ${inventory.formalSbom ? 'SBOM' : 'não é SBOM formal'}</strong></div>
  </div><p><small>${esc(build.note)}</small></p>`;

  q('#hash-proof').innerHTML = `<h3>Hashes</h3>
    <p><strong>Dossiê</strong></p><div class="hash">${esc(artifact.dossierSha256)}</div>
    <p><strong>Evidence Pack</strong></p><div class="hash">${esc(pack.packageSha256)}</div>
    <p><strong>Manifesto runtime</strong></p><div class="hash">${esc(release.manifestSha256)}</div>
    <p><strong>Libraries runtime</strong></p><div class="hash">${esc(release.payload.runtimeLibrariesSha256)}</div>
    <p><strong>Inventário POC de dependências</strong></p><div class="hash">${esc(inventory.sha256 || 'ausente')}</div>
    <p><small>${esc(artifact.hashAlgorithm)} · ${esc(artifact.canonicalization)} · inventário POC ≠ SBOM formal</small></p>`;

  q('#blocks').innerHTML = blocks.map(block => `<article class="block-line">
    <span class="num">${String(block.block).padStart(2, '0')}</span>
    <strong>${esc(block.name)}</strong>
    <span class="${block.passed ? 'ok' : 'attention'}">${block.passed ? 'PASSOU' : 'ATENÇÃO'}</span>
    <span>${esc(block.score)}%</span>
    <span class="route">${esc(block.uiRoute)}</span>
  </article>`).join('');

  q('#checks').innerHTML = preflight.checks.map(check => `<article class="module">
    <h3>${esc(check.name)}</h3><div class="kpi ${check.passed ? 'ok' : 'attention'}">${check.passed ? 'OK' : '!'}</div><p>${esc(check.detail)}</p>
  </article>`).join('');

  q('#blockers').innerHTML = blockers.length
    ? blockers.map(item => `<div class="risk"><strong>${esc(item.id)} · ${esc(item.description)}</strong><span>${esc(item.owner)} · ${esc(item.severity)}</span></div>`).join('')
    : '<article class="module"><h3>Nenhum blocker registrado</h3></article>';

  q('#disclaimers').innerHTML = `<strong>Limites da evidência</strong><ul>${payload.disclaimers.map(x => `<li>${esc(x)}</li>`).join('')}</ul>`;
  clearError();
}

async function generate() {
  const button = q('#generate');
  button.disabled = true;
  button.textContent = 'Gerando…';
  try {
    const artifact = await api('/api/poc/dossier', { method: 'POST', body: JSON.stringify({ actor: 'console.dossier', refreshPreflight: true }) });
    render(artifact);
    await verify();
  } catch (error) {
    showError(error);
  } finally {
    button.disabled = false;
    button.textContent = 'Gerar dossiê';
  }
}

async function verify() {
  if (!current) return showError(new Error('Gere ou carregue um dossiê primeiro.'));
  try {
    const result = await api(`/api/poc/dossier/${encodeURIComponent(current.verificationCode)}/verify`);
    q('#verification-result').innerHTML = `<article class="module">
      <h3>${result.integrityReady ? 'INTEGRIDADE APROVADA' : 'ATENÇÃO NA INTEGRIDADE'}</h3>
      <div class="rows">
        <div class="row"><span>hash do dossiê</span><strong>${result.dossierHashValid ? 'OK' : 'FALHA'}</strong></div>
        <div class="row"><span>código de verificação</span><strong>${result.verificationCodeValid ? 'OK' : 'FALHA'}</strong></div>
        <div class="row"><span>Evidence Pack</span><strong>${result.evidencePackHashValid ? 'OK' : 'FALHA'}</strong></div>
        <div class="row"><span>Evidence Ledger</span><strong>${result.evidenceLedgerValid ? 'OK' : 'FALHA'}</strong></div>
        <div class="row"><span>manifesto runtime</span><strong>${result.releaseManifestHashValid ? 'OK' : 'FALHA'}</strong></div>
        <div class="row"><span>bytes runtime</span><strong>${result.runtimeFilesValid ? 'OK' : 'FALHA'}</strong></div>
        <div class="row"><span>preflight</span><strong>${result.preflightReady ? 'READY' : 'ATENÇÃO'}</strong></div>
        <div class="row"><span>build vinculado</span><strong>${result.buildRevisionBound ? esc(result.sourceRevision?.slice(0, 12)) : 'não injetado'}</strong></div>
      </div><p><small>${esc(result.note)}</small></p>
    </article>`;
    clearError();
    return result;
  } catch (error) {
    showError(error);
  }
}

async function exportJson() {
  if (!current) return showError(new Error('Gere ou carregue um dossiê primeiro.'));
  try {
    const response = await fetch(`/api/poc/dossier/${encodeURIComponent(current.verificationCode)}/export`, { headers: authHeaders() });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const blob = await response.blob();
    const disposition = response.headers.get('content-disposition') || '';
    const match = disposition.match(/filename="?([^";]+)"?/i);
    const filename = match?.[1] || `jundiai-dossie-${current.verificationCode}.json`;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename; document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
  } catch (error) {
    showError(error);
  }
}

async function loadLatest() {
  try {
    const response = await fetch('/api/poc/dossier/latest', { headers: authHeaders({ 'Content-Type': 'application/json' }) });
    if (response.status === 404) return;
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    render(await response.json());
  } catch (error) {
    showError(error);
  }
}

q('#generate').addEventListener('click', generate);
q('#verify').addEventListener('click', verify);
q('#export').addEventListener('click', exportJson);
q('#print').addEventListener('click', () => window.print());
loadLatest();
