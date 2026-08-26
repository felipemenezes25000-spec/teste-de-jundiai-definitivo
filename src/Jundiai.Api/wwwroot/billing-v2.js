const $ = s => document.querySelector(s);
const esc = v => String(v ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const session = () => localStorage.getItem('jundiai.session');
const role = () => localStorage.getItem('jundiai.role') || 'poc_admin';

async function api(url, options={}) {
  const token = session();
  const headers = {
    'Content-Type':'application/json',
    ...(token ? {Authorization:`Bearer ${token}`} : {'X-Demo-Role':role(),'X-Demo-User':localStorage.getItem('jundiai.user') || 'poc.operador'}),
    ...(options.headers||{})
  };
  const response = await fetch(url,{...options,headers});
  const type = response.headers.get('content-type')||'';
  const body = type.includes('application/json') ? await response.json().catch(()=>({})) : await response.text();
  if(!response.ok) throw new Error(body?.detail || body?.title || `HTTP ${response.status}`);
  return body;
}

function showError(message){ const el=$('#error'); el.hidden=!message; el.textContent=message||''; }
function statusLabel(s){ return ({draft:'rascunho',validated:'validado',criticized:'com críticas',closed:'fechado',superseded:'substituído'})[s] || s; }
function fmtDate(v){ return v ? new Date(v).toLocaleString('pt-BR') : '—'; }
function currentCompetence(){ const d=new Date(); return `${d.getFullYear()}${String(d.getMonth()+1).padStart(2,'0')}`; }

function renderProduction(items){
  $('#prod-count').textContent=items.length;
  $('#production').innerHTML = items.length ? items.map(x=>`<div class="prod"><div><strong>${esc(x.citizenName)}</strong><small>${esc(x.procedureCode)} · ${esc(x.procedureName)} · ${esc(x.cbo)} · ${esc(x.cid || 'sem CID')}</small></div><div><strong>${esc(x.billingForm)}</strong><small>${esc(x.serviceDate)}${x.tooth ? ` · dente ${x.tooth}`:''}${x.sextant ? ` · sextante ${x.sextant}`:''}</small></div></div>`).join('') : '<p>Sem produção nesta instância.</p>';
}

function renderCatalog(items){
  $('#catalog').innerHTML = items.map(x=>`<article><strong>${esc(x.code)} · ${esc(x.name)}</strong><p>${esc(x.billingForm)} · idade ${x.minAge}-${x.maxAge}</p><small>CBO: ${esc(x.allowedCboPrefixes.join(', '))}${x.requiresTooth?' · requer dente':''}${x.requiresSextant?' · requer sextante':''}</small></article>`).join('');
}

async function batchHistory(id){
  const history=await api(`/api/sus/billing/v2/batches/${id}/history`);
  const target=document.querySelector(`[data-history="${id}"]`);
  if(target) target.innerHTML=history.map(x=>`<div class="mono">${esc(fmtDate(x.occurredAt))} · ${esc(x.action)} · ${esc(x.detail)}</div>`).join('') || '<div class="mono">sem eventos</div>';
}

async function exportBatch(id){
  try{
    showError('');
    const x=await api(`/api/sus/billing/v2/batches/${id}/export`);
    const target=document.querySelector(`[data-export="${id}"]`);
    if(target) target.innerHTML=`<div class="mono"><strong>${esc(x.format)}</strong><br>SHA-256 ${esc(x.sha256)}<br>${esc(x.lines.join('\n'))}<br><br>${esc(x.disclaimer)}</div>`;
  }catch(e){showError(e.message);}
}

async function action(id, name){
  try{
    showError('');
    if(name==='reopen'){
      await api(`/api/sus/billing/v2/batches/${id}/reopen`,{method:'POST',body:JSON.stringify({reason:'Revisão demonstrativa controlada',actor:'faturamento.poc'})});
    } else {
      await api(`/api/sus/billing/v2/batches/${id}/${name}`,{method:'POST',body:'{}'});
    }
    await load();
  }catch(e){showError(e.message);}
}

function renderBatches(items){
  $('#batches').innerHTML = items.length ? items.map(x=>`<article class="batch ${esc(x.status)}"><div class="section-head"><div><h3>${esc(x.competence)} · v${x.version}</h3><p>${esc(statusLabel(x.status))} · ${x.productionIds.length} item(ns) · ${x.issues.length} crítica(s)</p></div><strong>${x.exportChecksum ? 'checksum OK' : 'aberto'}</strong></div>${x.issues.map(i=>`<div class="issue"><strong>${esc(i.code)}</strong> · ${esc(i.message)}</div>`).join('')}${x.exportChecksum?`<div class="mono">SHA-256 ${esc(x.exportChecksum)}</div>`:''}<div class="actions"><button onclick="action('${x.id}','validate')">Revalidar</button><button onclick="action('${x.id}','close')">Fechar</button><button onclick="action('${x.id}','reopen')">Reabrir</button><button class="ghost" onclick="exportBatch('${x.id}')">Exportar</button><button class="ghost" onclick="batchHistory('${x.id}')">Histórico</button></div><div data-export="${x.id}"></div><div data-history="${x.id}"></div></article>`).join('') : '<article class="batch"><h3>Nenhum lote criado</h3><p>Use a competência atual para demonstrar crítica e fechamento.</p></article>';
}

async function load(){
  try{
    showError('');
    const [production,batches,catalog]=await Promise.all([
      api('/api/sus/billing/v2/production'),
      api('/api/sus/billing/v2/batches'),
      api('/api/sus/sigtap')
    ]);
    renderProduction(production); renderBatches(batches); renderCatalog(catalog);
  }catch(e){showError(e.message);}
}

$('#competence').value=currentCompetence();
$('#create-batch').addEventListener('click',async()=>{
  try{
    showError('');
    const competence=$('#competence').value.trim();
    if(!/^\d{6}$/.test(competence)) throw new Error('Competência deve estar em AAAAMM.');
    await api('/api/sus/billing/v2/batches',{method:'POST',body:JSON.stringify({competence})});
    await load();
  }catch(e){showError(e.message);}
});
$('#refresh').addEventListener('click',load);
window.action=action; window.exportBatch=exportBatch; window.batchHistory=batchHistory;
load();
