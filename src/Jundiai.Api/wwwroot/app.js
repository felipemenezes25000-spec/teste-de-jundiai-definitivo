const $ = (selector) => document.querySelector(selector);
const content = $('#content');
const title = $('#view-title');
const toast = $('#toast');

const api = async (url, options = {}) => {
  const response = await fetch(url, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      'X-Demo-User': 'poc.operador',
      'X-Demo-Role': 'poc_admin',
      ...(options.headers || {})
    }
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || problem.title || `Erro ${response.status}`);
  }
  const type = response.headers.get('content-type') || '';
  return type.includes('application/json') ? response.json() : response.text();
};

const fmtDate = (value) => value ? new Date(value).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '—';
const esc = (value) => String(value ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]));
const badge = (text) => `<span class="badge ${esc(text)}">${esc(text)}</span>`;
const table = (headers, rows) => `<div class="table-wrap"><table><thead><tr>${headers.map(h => `<th>${h}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`;

function showToast(message, type = 'ok') {
  toast.textContent = message;
  toast.dataset.type = type;
  toast.hidden = false;
  clearTimeout(showToast.timer);
  showToast.timer = setTimeout(() => toast.hidden = true, 3200);
}

function metric(label, value, sub) {
  return `<div class="metric"><span>${esc(label)}</span><strong>${esc(value)}</strong><small>${esc(sub)}</small></div>`;
}

async function overview() {
  title.textContent = 'Centro de comando municipal';
  const [d, regulation, exams] = await Promise.all([api('/api/dashboard'), api('/api/regulation'), api('/api/diagnostics/exams')]);
  content.innerHTML = `
    <div class="hero-card">
      <div><p class="eyebrow">Jornada pública integrada</p><h2>Da UBS ao faturamento, com rastreabilidade</h2><p>Base independente para a POC do RCE 008/2026, consolidando módulos assistenciais, regulatórios, territoriais, farmacêuticos e fiscais.</p></div>
      <div class="hero-tag">POC navegável</div>
    </div>
    <div class="metrics">
      ${metric('Cidadãos', d.citizens, 'cadastro territorial')}
      ${metric('Fila regulada', d.waitingRegulation, 'aguardando destino')}
      ${metric('Agenda hoje', d.scheduledToday, 'consultas reguladas')}
      ${metric('Críticas BPA', d.openBillingIssues, 'impedimentos atuais')}
      ${metric('Baixo estoque', d.lowStockLots, 'lotes abaixo do mínimo')}
      ${metric('Exames hoje', d.examsToday, 'laboratório/imagem')}
    </div>
    <div class="grid two">
      <section class="card"><div class="card-head"><h3>Fila regulatória</h3><button onclick="navigate('regulation')">Abrir módulo</button></div>
        ${table(['Cidadão','Especialidade','Prioridade','Status'], regulation.slice(0,5).map(x => `<tr><td>${esc(x.citizenName)}</td><td>${esc(x.specialty)}</td><td>${badge(x.priority)}</td><td>${badge(x.status)}</td></tr>`))}
      </section>
      <section class="card"><div class="card-head"><h3>Exames</h3><button onclick="navigate('diagnostics')">Abrir módulo</button></div>
        ${table(['Cidadão','Exame','Tipo','Status'], exams.slice(0,5).map(x => `<tr><td>${esc(x.citizenName)}</td><td>${esc(x.exam)}</td><td>${esc(x.type)}</td><td>${badge(x.status)}</td></tr>`))}
      </section>
    </div>`;
}

async function clinical() {
  title.textContent = 'Prontuário longitudinal · Patient 360';
  const [citizens, workspaces] = await Promise.all([api('/api/citizens'), api('/api/clinical/workspaces')]);
  const citizen = citizens[0];
  const summary = await api(`/api/clinical/patients/${citizen.id}/summary`);
  const p = summary.profile;
  content.innerHTML = `
    <div class="split-heading"><div><p class="eyebrow">Contexto clínico unificado</p><h2>${esc(citizen.name)}</h2></div>${badge(citizen.healthUnit)}</div>
    <div class="metrics clinical-metrics">
      ${metric('PA', p.lastVitals.bloodPressure, 'última aferição')}
      ${metric('FC', `${p.lastVitals.heartRate} bpm`, 'frequência cardíaca')}
      ${metric('SpO₂', `${p.lastVitals.spo2}%`, 'saturação')}
      ${metric('Peso', `${p.lastVitals.weightKg} kg`, 'antropometria')}
    </div>
    <div class="grid two">
      <section class="card"><h3>Resumo clínico</h3>
        <div class="clinical-block"><strong>Condições</strong>${p.conditions.map(x => `<span>${esc(x)}</span>`).join('')}</div>
        <div class="clinical-block"><strong>Alergias</strong>${p.allergies.length ? p.allergies.map(x => `<span class="warning-text">${esc(x)}</span>`).join('') : '<span>Nenhuma alergia documentada</span>'}</div>
        <div class="clinical-block"><strong>Medicamentos</strong>${p.medications.length ? p.medications.map(x => `<span>${esc(x)}</span>`).join('') : '<span>Sem medicamentos ativos nesta demonstração</span>'}</div>
        <div class="clinical-block"><strong>Alertas</strong>${p.alerts.length ? p.alerts.map(x => `<span class="warning-text">${esc(x)}</span>`).join('') : '<span>Sem alertas ativos</span>'}</div>
      </section>
      <section class="card"><h3>Workspaces profissionais</h3><div class="workspace-grid">${workspaces.map(w => `<article class="workspace"><strong>${esc(w.label)}</strong><small>${esc(w.council)}</small><p>${esc(w.clinicalFocus)}</p><span>${w.documents.map(d => esc(d)).join(' · ')}</span></article>`).join('')}</div></section>
    </div>
    <section class="card"><div class="card-head"><h3>Linha do tempo</h3><span class="badge">${summary.timeline.length} registros</span></div>
      <div class="timeline">${summary.timeline.map(e => `<article><div class="timeline-dot"></div><div><strong>${esc(e.professionLabel)} · ${esc(e.professional)}</strong><small>${fmtDate(e.occurredAt)}</small><p><b>Avaliação:</b> ${esc(e.assessment)}</p><p><b>Plano:</b> ${esc(e.plan)}</p><span>${e.diagnoses.map(d => esc(d)).join(' · ')}</span></div></article>`).join('')}</div>
    </section>
    <div class="notice">Este Patient 360 reaproveita o princípio do RenoveJá de consolidar prontuário, contexto pré-consulta e atuação multiprofissional sem dar escrita clínica ao gestor por efeito colateral.</div>`;
}

async function regulation() {
  title.textContent = 'Regulação e agendamento';
  const rows = await api('/api/regulation');
  content.innerHTML = `<section class="card"><div class="card-head"><div><p class="eyebrow">Fila regulada com trilha explícita</p><h2>Regulação</h2></div></div>
    ${table(['Cidadão','Origem','Especialidade','Prioridade','Status','Solicitado','Destino'], rows.map(x => `<tr><td>${esc(x.citizenName)}</td><td>${esc(x.originUnit)}</td><td>${esc(x.specialty)}</td><td>${badge(x.priority)}</td><td>${badge(x.status)}</td><td>${fmtDate(x.requestedAt)}</td><td>${esc(x.destinationUnit || 'A definir')}</td></tr>`))}
  </section>`;
}

async function billing() {
  title.textContent = 'Faturamento SUS';
  const [production, batches] = await Promise.all([api('/api/sus/production'), api('/api/sus/billing/batches')]);
  content.innerHTML = `<div class="split-heading"><div><p class="eyebrow">Produção → crítica → fechamento</p><h2>BPA / e-SUS</h2></div><button class="primary" id="create-batch">Criar lote da competência atual</button></div>
    <div class="grid two"><section class="card"><h3>Produção nominal</h3>${table(['Paciente','Procedimento','CBO','CID','Valor'], production.map(x => `<tr><td>${esc(x.citizenName)}</td><td>${esc(x.procedureCode)}</td><td>${esc(x.cbo)}</td><td>${esc(x.cid)}</td><td>R$ ${Number(x.amount).toFixed(2)}</td></tr>`))}</section>
    <section class="card"><h3>Lotes</h3>${batches.length ? table(['Competência','Status','Itens','Críticas','Ações'], batches.map(x => `<tr><td>${esc(x.competence)}</td><td>${badge(x.status)}</td><td>${x.items.length}</td><td>${x.issues.length}</td><td><button onclick="closeBatch('${x.id}')">Fechar</button></td></tr>`)) : '<p class="empty">Nenhum lote criado nesta sessão.</p>'}</section></div>
    <div class="notice">A exportação atual é uma <strong>demonstração estrutural</strong>; não é declarada como arquivo oficial DATASUS até implantação dos layouts oficiais aplicáveis.</div>`;
  $('#create-batch').onclick = async () => {
    const now = new Date();
    const competence = `${now.getFullYear()}${String(now.getMonth()+1).padStart(2,'0')}`;
    try { await api('/api/sus/billing/batches', { method:'POST', body:JSON.stringify({ competence }) }); showToast('Lote criado e criticado.'); await billing(); } catch(e) { showToast(e.message,'error'); }
  };
}

window.closeBatch = async id => {
  try { await api(`/api/sus/billing/batches/${id}/close`, { method:'POST' }); showToast('Lote fechado com sucesso.'); await billing(); } catch(e) { showToast(e.message,'error'); }
};

async function immunization() {
  title.textContent = 'Imunização';
  const [lots, history] = await Promise.all([api('/api/immunization/lots'), api('/api/immunization/history')]);
  content.innerHTML = `<div class="grid two"><section class="card"><h2>Lotes vacinais</h2>${table(['Vacina','Fabricante','Lote','Validade','Estoque'], lots.map(x => `<tr><td>${esc(x.vaccine)}</td><td>${esc(x.manufacturer)}</td><td>${esc(x.lot)}</td><td>${esc(x.expiresOn)}</td><td>${x.stock}</td></tr>`))}</section>
    <section class="card"><h2>Histórico de aplicações</h2>${history.length ? table(['Cidadão','Vacina','Dose','Lote','Profissional'], history.map(x => `<tr><td>${esc(x.citizenName)}</td><td>${esc(x.vaccine)}</td><td>${esc(x.dose)}</td><td>${esc(x.lot)}</td><td>${esc(x.professional)}</td></tr>`)) : '<p class="empty">Ainda não há aplicações nesta sessão.</p>'}</section></div>
    <div class="notice">O domínio controla lote, validade, via, local, dose, profissional e baixa automática de estoque. RNDS/SI-PNI permanecem dependentes de contrato e homologação oficiais.</div>`;
}

async function pharmacy() {
  title.textContent = 'Farmácia, estoque e dispensação';
  const [inventory, movements] = await Promise.all([api('/api/pharmacy/inventory'), api('/api/pharmacy/movements')]);
  content.innerHTML = `<section class="card"><div class="card-head"><div><p class="eyebrow">Lote, validade e estoque mínimo</p><h2>Posição de estoque</h2></div></div>${table(['Item','Unidade','Lote','Validade','Qtd.','Mínimo','Controle'], inventory.map(x => `<tr class="${x.quantity <= x.minimumStock ? 'alert-row' : ''}"><td>${esc(x.name)}</td><td>${esc(x.unit)}</td><td>${esc(x.lot)}</td><td>${esc(x.expiresOn)}</td><td>${x.quantity}</td><td>${x.minimumStock}</td><td>${x.controlled ? badge('controlled') : 'comum'}</td></tr>`))}</section>
    <section class="card"><h3>Movimentações</h3>${movements.length ? table(['Item','Tipo','Qtd.','Ator','Data'], movements.map(x => `<tr><td>${esc(x.itemName)}</td><td>${esc(x.type)}</td><td>${x.quantity}</td><td>${esc(x.actor)}</td><td>${fmtDate(x.occurredAt)}</td></tr>`)) : '<p class="empty">Nenhuma movimentação nesta sessão.</p>'}</section>`;
}

async function psf() {
  title.textContent = 'PSF, território e ACS';
  const [households, visits] = await Promise.all([api('/api/psf/households'), api('/api/psf/acs/visits')]);
  content.innerHTML = `<div class="split-heading"><div><p class="eyebrow">Área → microárea → domicílio → família</p><h2>Território</h2></div><a class="button primary" href="/acs.html">App ACS offline</a></div>
    <div class="grid two"><section class="card"><h3>Domicílios</h3>${households.map(h => `<article class="household"><strong>${esc(h.address)}</strong><span>Área ${esc(h.area)} · Microárea ${esc(h.microArea)} · ACS ${esc(h.acsName)}</span><small>${h.members.map(m => esc(m.name)).join(' · ')}</small></article>`).join('')}</section>
    <section class="card"><h3>Visitas sincronizadas</h3>${visits.length ? table(['ACS','Tipo','Desfecho','Offline','Data'], visits.map(x => `<tr><td>${esc(x.acsName)}</td><td>${esc(x.visitType)}</td><td>${esc(x.outcome)}</td><td>${x.offlineCaptured ? 'sim' : 'não'}</td><td>${fmtDate(x.occurredAt)}</td></tr>`)) : '<p class="empty">Abra o app ACS, registre offline e sincronize.</p>'}</section></div>`;
}

async function dental() {
  title.textContent = 'Odontologia';
  const citizens = await api('/api/citizens');
  const citizen = citizens[2] || citizens[0];
  const chart = await api(`/api/dental/${citizen.id}/odontogram`);
  const teeth = Object.values(chart.teeth).sort((a,b) => a.tooth-b.tooth);
  content.innerHTML = `<section class="card"><div class="card-head"><div><p class="eyebrow">Odontograma estruturado</p><h2>${esc(citizen.name)}</h2></div><span class="badge">32 elementos</span></div>
    <div class="odontogram">${teeth.map(t => `<button class="tooth ${t.status !== 'healthy' ? 'problem' : ''}" title="${esc(t.notes || t.status)}"><span>${t.tooth}</span><b>${t.status === 'healthy' ? '✓' : '!'}</b><small>${esc(t.surfaces || '')}</small></button>`).join('')}</div>
    <div class="notice">O modelo registra elemento, faces, status, procedimento, observação, profissional e histórico temporal. Próxima evolução: representação gráfica por coroa/raiz e faturamento por elemento/sextante.</div></section>`;
}

async function diagnostics() {
  title.textContent = 'Exames laboratoriais e imagem';
  const exams = await api('/api/diagnostics/exams');
  content.innerHTML = `<section class="card"><h2>Agenda e fila de exames</h2>${table(['Cidadão','Tipo','Exame','Status','Agendamento','Unidade','Executor'], exams.map(x => `<tr><td>${esc(x.citizenName)}</td><td>${esc(x.type)}</td><td>${esc(x.exam)}</td><td>${badge(x.status)}</td><td>${fmtDate(x.scheduledAt)}</td><td>${esc(x.unit)}</td><td>${esc(x.performer || '—')}</td></tr>`))}</section>`;
}

async function audit() {
  title.textContent = 'Auditoria e evidências';
  const rows = await api('/api/audit');
  content.innerHTML = `<section class="card"><div class="card-head"><div><p class="eyebrow">Trilha operacional</p><h2>Eventos auditáveis</h2></div></div>${table(['Ator','Ação','Recurso','Detalhe','Data'], rows.map(x => `<tr><td>${esc(x.actor)}</td><td><code>${esc(x.action)}</code></td><td>${esc(x.resource)}</td><td>${esc(x.detail || '—')}</td><td>${fmtDate(x.occurredAt)}</td></tr>`))}</section>`;
}

const views = { overview, clinical, regulation, billing, immunization, pharmacy, psf, dental, diagnostics, audit };
window.navigate = async view => {
  document.querySelectorAll('#nav button').forEach(b => b.classList.toggle('active', b.dataset.view === view));
  content.innerHTML = '<div class="loading">Carregando…</div>';
  try { await views[view](); history.replaceState(null, '', `#${view}`); } catch(e) { content.innerHTML = `<div class="error-card"><h2>Não foi possível carregar</h2><p>${esc(e.message)}</p></div>`; }
};

document.querySelectorAll('#nav button').forEach(button => button.onclick = () => navigate(button.dataset.view));
window.addEventListener('load', () => navigate(location.hash.slice(1) in views ? location.hash.slice(1) : 'overview'));
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => {});
