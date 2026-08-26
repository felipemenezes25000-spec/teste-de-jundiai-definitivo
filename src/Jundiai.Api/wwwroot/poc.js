const q = s => document.querySelector(s);
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const session = () => localStorage.getItem('jundiai.session');
const role = () => localStorage.getItem('jundiai.role') || 'poc_admin';

async function api(url, options = {}) {
  const token = session();
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : { 'X-Demo-Role': role(), 'X-Demo-User': localStorage.getItem('jundiai.user') || 'poc.operador' }),
    ...(options.headers || {})
  };
  const response = await fetch(url, { ...options, headers });
  const type = response.headers.get('content-type') || '';
  const body = type.includes('application/json') ? await response.json().catch(() => ({})) : await response.text();
  if (!response.ok) throw new Error(body?.detail || body?.title || `HTTP ${response.status}`);
  return body;
}

function statusLabel(status) {
  return ({ implemented_poc: 'POC implementada', partial: 'Parcial', external: 'Externo', pending: 'Pendente' })[status] || status;
}

function blockCard(definition, state) {
  const status = state?.status || 'pending';
  return `<article class="block" id="${esc(definition.slug)}">
    <div class="num">${String(definition.number).padStart(2,'0')}</div>
    <div>
      <h3>${esc(definition.name)}</h3>
      <p>${esc(state?.evidence || 'Aguardando leitura de readiness.')}</p>
      <div class="capabilities">${definition.capabilities.map(x => `<span>${esc(x)}</span>`).join('')}</div>
    </div>
    <span class="status ${esc(status)}">${esc(statusLabel(status))} · ${state?.score ?? 0}%</span>
  </article>`;
}

function rows(items, mapper) {
  if (!items?.length) return '<p>Nenhum registro nesta instância.</p>';
  return `<div class="rows">${items.slice(0,7).map(mapper).join('')}</div>`;
}

async function load() {
  document.body.classList.add('loading');
  try {
    const [definition, readiness, security, scheduling, diagnostics, inventory, telemedicine, evidence, blockers, dashboard, billing, documents] = await Promise.all([
      api('/api/contract/jundiai'),
      api('/api/contract/jundiai/readiness'),
      api('/api/security/readiness'),
      api('/api/scheduling/readiness'),
      api('/api/diagnostics/v2/readiness'),
      api('/api/inventory/v2/readiness'),
      api('/api/telemedicine/readiness'),
      api('/api/evidence/verify'),
      api('/api/contract/jundiai/non-code-blockers'),
      api('/api/dashboard'),
      api('/api/sus/billing/v2/production'),
      api('/api/documents/readiness')
    ]);

    q('#score').style.setProperty('--score', `${readiness.overallScore}%`);
    q('#score-value').textContent = readiness.overallScore;
    q('#generated').textContent = new Date(readiness.generatedAt).toLocaleString('pt-BR');
    q('#blocks').innerHTML = definition.blocks.map(block => blockCard(block, readiness.blocks.find(x => x.block === block.number))).join('');

    const implemented = readiness.blocks.filter(x => x.status === 'implemented_poc').length;
    const partial = readiness.blocks.filter(x => x.status === 'partial').length;
    q('#summary').innerHTML = [
      ['Blocos POC', `${implemented}/14`, 'executáveis'],
      ['Parciais', partial, 'integração/profundidade'],
      ['Unidades demo', 58, 'diretório municipal POC'],
      ['Produção SUS', billing.length, 'itens no motor v2'],
      ['Alertas estoque', inventory.alerts, 'atenção operacional']
    ].map(([label,value,sub]) => `<div class="metric"><small>${esc(label)}</small><strong>${esc(value)}</strong><small>${esc(sub)}</small></div>`).join('');

    q('#security').innerHTML = `<h3>Segurança</h3><div class="kpi">${security.seededUsers}</div><p>identidades demonstrativas · ${esc(security.rbac)}</p><div class="rows"><div class="row"><span>Senha</span><strong>${esc(security.passwordHash)}</strong></div><div class="row"><span>MFA</span><strong>ativo</strong></div><div class="row"><span>Lockout</span><strong>${security.lockout.attempts} tentativas</strong></div></div>`;
    q('#scheduling').innerHTML = `<h3>Agenda central</h3><div class="kpi">${scheduling.slotCount}</div><p>slots gerados com ${scheduling.gridCount} grades e ${scheduling.quotaCount} políticas de cota.</p>${rows(scheduling.capabilities, x => `<div class="row"><span>capability</span><strong>${esc(x)}</strong></div>`)}`;
    q('#diagnostics').innerHTML = `<h3>Lab + imagem</h3><div class="kpi">${diagnostics.orderCount}</div><p>pedido(s) no motor diagnóstico avançado.</p>${rows(diagnostics.capabilities, x => `<div class="row"><span>fluxo</span><strong>${esc(x)}</strong></div>`)}`;
    q('#inventory').innerHTML = `<h3>Estoque avançado</h3><div class="kpi">${inventory.lots}</div><p>lotes · ${inventory.alerts} alerta(s) operacional(is).</p>${rows(inventory.capabilities, x => `<div class="row"><span>controle</span><strong>${esc(x)}</strong></div>`)}`;
    q('#telemedicine').innerHTML = `<h3>Telemedicina</h3><div class="kpi">${telemedicine.implemented.length}</div><p>capacidades do fluxo de sala de espera e atendimento.</p>${rows(telemedicine.implemented, x => `<div class="row"><span>tele</span><strong>${esc(x)}</strong></div>`)}`;
    q('#evidence').innerHTML = `<h3>Evidência</h3><div class="kpi">${evidence.valid ? 'OK' : '!'}</div><p>${esc(evidence.message)}</p><div class="row"><span>eventos verificados</span><strong>${evidence.checkedEvents}</strong></div><div class="evidence">cadeia SHA-256 · ${evidence.valid ? 'íntegra' : 'divergente'}</div>`;
    q('#documents').innerHTML = `<h3>Documentos clínicos</h3><div class="kpi">${documents.documents}</div><p>Hash SHA-256 + envelope de assinatura demonstrativa.</p>${rows(documents.supported, x => `<div class="row"><span>documento</span><strong>${esc(x)}</strong></div>`)}`;
    q('#dashboard-module').innerHTML = `<h3>Operação municipal</h3><div class="kpi">${dashboard.citizens}</div><p>cidadãos no cenário POC.</p><div class="rows"><div class="row"><span>fila regulação</span><strong>${dashboard.waitingRegulation}</strong></div><div class="row"><span>exames hoje</span><strong>${dashboard.examsToday}</strong></div><div class="row"><span>baixo estoque</span><strong>${dashboard.lowStockLots}</strong></div></div>`;

    q('#risks').innerHTML = blockers.map(x => `<div class="risk"><strong>${esc(x.id)} · ${esc(x.description)}</strong><span>${esc(x.owner)} · ${esc(x.severity)}</span></div>`).join('');
    q('#critical-notes').innerHTML = readiness.criticalNotes.map(x => `<li>${esc(x)}</li>`).join('');
  } catch (error) {
    q('#error').hidden = false;
    q('#error').textContent = error.message;
  } finally {
    document.body.classList.remove('loading');
  }
}

q('#refresh').addEventListener('click', load);
q('#logout').addEventListener('click', async () => {
  try { if (session()) await api('/api/auth/logout', { method: 'POST' }); } catch {}
  localStorage.removeItem('jundiai.session');
  localStorage.removeItem('jundiai.role');
  localStorage.removeItem('jundiai.user');
  location.href = '/login.html';
});
load();
