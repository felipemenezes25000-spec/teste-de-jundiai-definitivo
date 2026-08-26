const persistenceApi = async (url, options={}) => {
  const token=localStorage.getItem('jundiai.session');
  const response=await fetch(url,{...options,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{'X-Demo-Role':'poc_admin','X-Demo-User':'poc.operador'}),...(options.headers||{})}});
  const body=await response.json().catch(()=>({}));
  if(!response.ok) throw new Error(body.detail||body.title||`HTTP ${response.status}`);
  return body;
};

const persistenceEscape=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

function renderPersistenceState(p,recovery){
  const k=document.querySelector('#persistence-kpis');
  const list=document.querySelector('#persistence-list');
  const basic=document.querySelector('#checkpoint');
  const full=document.querySelector('#checkpoint-full');
  const drill=document.querySelector('#recovery-drill');
  if(!k||!list||!basic)return;
  k.innerHTML=[
    ['Provider',p.provider||'PostgreSQL'],
    ['Configurado',p.configured?'sim':'não'],
    ['Conexão',p.canConnect?'OK':'—'],
    ['Checkpoints',recovery?.checkpoints??0]
  ].map(([label,value])=>`<article><small>${label}</small><strong style="font-size:16px">${persistenceEscape(value)}</strong></article>`).join('');
  const migrations=Array.isArray(p.pendingMigrations)?p.pendingMigrations:[];
  const recoveryCapabilities=Array.isArray(recovery?.capabilities)?recovery.capabilities:[];
  list.innerHTML=`<article class="item"><div class="item-head"><div><h3>Fundação PostgreSQL</h3><p>${persistenceEscape(p.note||'DbContext, migration, tenant scope, checkpoint, outbox e idempotência.')}</p></div><span class="chip">${p.configured?'configurado':'fallback POC'}</span></div><div class="mono">migrations pendentes: ${migrations.length?migrations.map(persistenceEscape).join(', '):'nenhuma/indisponível'}</div></article>
  <article class="item"><div class="item-head"><div><h3>Recovery control plane</h3><p>Checkpoint completo por domínio, manifesto SHA-256, verificação de envelopes e preview de restauração sem mutação.</p></div><span class="chip">${recovery?.recoveryDrillAvailable?'drill disponível':'sem checkpoint'}</span></div><div class="mono">${persistenceEscape(recoveryCapabilities.join(' · ')||'Configure PostgreSQL para habilitar.')}</div></article>`;
  basic.disabled=!p.configured;
  if(full) full.disabled=!p.configured;
  if(drill) drill.disabled=!p.configured||!recovery?.recoveryDrillAvailable;
  basic.title=p.configured?'Cria snapshot transacional resumido':'Configure ConnectionStrings:Jundiai antes de criar checkpoint';
}

async function refreshPersistence(){
  try{
    const [p,recovery]=await Promise.all([
      persistenceApi('/api/audit/persistence/readiness'),
      persistenceApi('/api/audit/persistence/recovery/readiness').catch(()=>null)
    ]);
    renderPersistenceState(p,recovery);
  }catch(e){const list=document.querySelector('#persistence-list');if(list)list.innerHTML=`<article class="item"><p>${persistenceEscape(e.message)}</p></article>`;}
}

function prependPersistenceCard(title,html){
  const list=document.querySelector('#persistence-list');
  if(list)list.insertAdjacentHTML('afterbegin',`<article class="item"><h3>${persistenceEscape(title)}</h3>${html}</article>`);
}

document.querySelector('#checkpoint')?.addEventListener('click',async()=>{
  try{
    const result=await persistenceApi('/api/audit/persistence/checkpoint',{method:'POST',body:JSON.stringify({label:'checkpoint-console-governanca'})});
    prependPersistenceCard('Checkpoint básico criado',`<p>${result.envelopeCount} envelopes · ${persistenceEscape(result.institutionId)}</p><div class="mono">${persistenceEscape(result.checkpointId)}</div>`);
    await refreshPersistence();
  }catch(e){prependPersistenceCard('Falha no checkpoint',`<p>${persistenceEscape(e.message)}</p>`);}
});

document.querySelector('#checkpoint-full')?.addEventListener('click',async()=>{
  try{
    const result=await persistenceApi('/api/audit/persistence/checkpoints/full',{method:'POST',body:JSON.stringify({label:'full-domain-console-governanca'})});
    prependPersistenceCard('Checkpoint completo criado',`<p>${result.envelopeCount} envelopes de domínio · ${persistenceEscape(result.institutionId)}</p><div class="mono">checkpoint ${persistenceEscape(result.checkpointId)}<br/>manifest SHA-256 ${persistenceEscape(result.manifestSha256)}</div>`);
    await refreshPersistence();
  }catch(e){prependPersistenceCard('Falha no checkpoint completo',`<p>${persistenceEscape(e.message)}</p>`);}
});

document.querySelector('#recovery-drill')?.addEventListener('click',async()=>{
  try{
    const result=await persistenceApi('/api/audit/persistence/recovery-drill',{method:'POST',body:JSON.stringify({checkpointId:null,actor:'console.governanca'})});
    const status=result.integrityValid&&result.restorePreviewValid&&result.criticalKindsPresent===result.criticalKindsExpected?'APROVADO':'ATENÇÃO';
    prependPersistenceCard(`Recovery drill · ${status}`,`<p>Integridade: ${result.integrityValid?'OK':'falha'} · restore preview: ${result.restorePreviewValid?'OK':'falha'} · críticos ${result.criticalKindsPresent}/${result.criticalKindsExpected} · RPO observado ${result.rpoAgeSeconds}s</p><div class="mono">checkpoint ${persistenceEscape(result.checkpointId)}${result.failures?.length?`<br/>falhas: ${result.failures.map(persistenceEscape).join(', ')}`:''}</div><p>${persistenceEscape(result.disclaimer)}</p>`);
  }catch(e){prependPersistenceCard('Falha no recovery drill',`<p>${persistenceEscape(e.message)}</p>`);}
});

refreshPersistence();
