const $ = s => document.querySelector(s);
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const token = () => localStorage.getItem('jundiai.session');

async function api(url) {
  const session = token();
  const response = await fetch(url, { headers: session ? { Authorization: `Bearer ${session}` } : { 'X-Demo-Role': 'poc_admin', 'X-Demo-User': 'poc.operador' } });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.detail || body.title || `HTTP ${response.status}`);
  return body;
}

function fmt(value) {
  return value ? new Date(value).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '—';
}

async function loadCitizens() {
  const citizens = await api('/api/citizens');
  $('#citizen').innerHTML = citizens.map(c => `<option value="${c.id}">${esc(c.name)} · ${esc(c.healthUnit)}</option>`).join('');
  if (citizens.length) await loadTrace(citizens[0].id);
}

async function loadTrace(id) {
  try {
    const graph = await api(`/api/care-trace/${id}`);
    $('#error').hidden = true;
    $('#node-count').textContent = graph.nodes.length;
    $('#title').textContent = graph.citizenName;
    $('#subtitle').textContent = `${graph.nodes.length} nós · ${graph.edges.length} relações · status de continuidade: ${graph.continuity.status}`;
    const root = graph.nodes.find(x => x.type === 'citizen');
    $('#patient').innerHTML = root ? `<h2>${esc(root.label)}</h2><p>CNS ${esc(root.metadata.cns)}<br>${esc(root.metadata.healthUnit)}<br>Área ${esc(root.metadata.area)} · Microárea ${esc(root.metadata.microArea)}</p>` : '';

    $('#gaps').innerHTML = graph.continuity.gaps.length
      ? graph.continuity.gaps.map(g => `<div class="gap ${esc(g.severity)}"><strong>${esc(g.severity)} · ${esc(g.domain)}</strong><span>${esc(g.description)}<br><b>Ação:</b> ${esc(g.suggestedOperationalAction)}</span></div>`).join('')
      : '<p class="empty">Nenhuma lacuna operacional detectada nesta instância.</p>';

    const events = graph.nodes.filter(x => x.type !== 'citizen').sort((a,b) => new Date(b.occurredAt) - new Date(a.occurredAt));
    $('#trace').innerHTML = events.length ? events.map(e => {
      const metadata = Object.entries(e.metadata || {}).filter(([,v]) => v !== '').map(([k,v]) => `<span>${esc(k)}: ${esc(v)}</span>`).join('');
      return `<article class="event"><div class="dot"></div><div class="event-body"><div class="type">${esc(e.type)}</div><h4>${esc(e.label)}</h4><small>${fmt(e.occurredAt)} · origem ${esc(e.sourceId)}</small><div class="meta">${metadata}</div></div></article>`;
    }).join('') : '<p class="empty">Ainda não há eventos derivados para este cidadão.</p>';
  } catch (error) {
    $('#error').hidden = false;
    $('#error').textContent = error.message;
  }
}

$('#citizen').addEventListener('change', event => loadTrace(event.target.value));
loadCitizens().catch(error => { $('#error').hidden = false; $('#error').textContent = error.message; });
