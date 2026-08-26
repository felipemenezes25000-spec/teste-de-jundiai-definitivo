const q = s => document.querySelector(s);
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const session = () => localStorage.getItem('jundiai.session');
const role = () => localStorage.getItem('jundiai.role') || 'poc_admin';
let current = null;

function authHeaders(extra = {}) {
  const token = session();
  return {
    ...(token ? { Authorization: `Bearer ${token}` } : { 'X-Demo-Role': role(), 'X-Demo-User': localStorage.getItem('jundiai.user') || 'poc.operador' }),
    ...extra
  };
}

async function api(url, options = {}) {
  const response = await fetch(url, { ...options, headers: authHeaders({ 'Content-Type': 'application/json', ...(options.headers || {}) }) });
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
  q('#verification-code').textContent = artifact.verificationCode;
  q('#manifest-hash').textContent = artifact.manifestSha256;
  q('#zip-hash').textContent = artifact.zipSha256;
  q('#summary').innerHTML = [
    ['Arquivos', artifact.fileCount, 'conteúdo do pacote'],
    ['Tamanho', `${Math.max(1, Math.round(artifact.zipBytes / 1024))} KB`, 'ZIP gerado em memória'],
    ['Dossiê', artifact.dossierVerificationCode, 'origem do snapshot'],
    ['Build', artifact.sourceRevision ? artifact.sourceRevision.slice(0, 12) : 'não injetado', artifact.validationRunId || 'run não informado']
  ].map(x => metric(...x)).join('');
  clearError();
}

function renderVerification(result) {
  const items = [
    ['Manifesto SHA-256', result.manifestHashValid],
    ['Código do kit', result.verificationCodeValid],
    ['Arquivos internos', result.entriesValid],
    ['ZIP SHA-256', result.zipHashValid],
    ['Dossiê íntegro', result.dossierIntegrityReady]
  ];
  q('#checks').innerHTML = items.map(([label, ok]) => `<article class="module"><h3>${esc(label)}</h3><div class="kpi ${ok ? 'ok' : 'attention'}">${ok ? 'OK' : '!'}</div><p>${ok ? 'confere' : 'divergência detectada'}</p></article>`).join('') + `<article class="module"><h3>${result.integrityReady ? 'KIT APROVADO' : 'ATENÇÃO'}</h3><p>${esc(result.note)}</p></article>`;
}

async function generate() {
  const button = q('#generate');
  button.disabled = true;
  button.textContent = 'Gerando…';
  try {
    const artifact = await api('/api/poc/contingency-bundle', { method: 'POST', body: JSON.stringify({ actor: 'console.contingency', refreshDossier: true }) });
    render(artifact);
    await verify();
  } catch (error) {
    showError(error);
  } finally {
    button.disabled = false;
    button.textContent = 'Gerar kit';
  }
}

async function verify() {
  if (!current) return showError(new Error('Gere ou carregue um kit primeiro.'));
  try {
    const result = await api(`/api/poc/contingency-bundle/${encodeURIComponent(current.verificationCode)}/verify`);
    renderVerification(result);
    clearError();
    return result;
  } catch (error) {
    showError(error);
  }
}

async function download() {
  if (!current) return showError(new Error('Gere ou carregue um kit primeiro.'));
  try {
    const response = await fetch(`/api/poc/contingency-bundle/${encodeURIComponent(current.verificationCode)}/download`, { headers: authHeaders() });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const blob = await response.blob();
    const disposition = response.headers.get('content-disposition') || '';
    const match = disposition.match(/filename="?([^";]+)"?/i);
    const filename = match?.[1] || `jundiai-contingencia-${current.verificationCode}.zip`;
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
    const response = await fetch('/api/poc/contingency-bundle/latest', { headers: authHeaders({ 'Content-Type': 'application/json' }) });
    if (response.status === 404) return;
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    render(await response.json());
  } catch (error) {
    showError(error);
  }
}

q('#generate').addEventListener('click', generate);
q('#verify').addEventListener('click', verify);
q('#download').addEventListener('click', download);
loadLatest();
