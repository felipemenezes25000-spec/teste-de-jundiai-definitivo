const persistenceApi = async (url, options={}) => {
  const token=localStorage.getItem('jundiai.session');
  const response=await fetch(url,{...options,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{'X-Demo-Role':'poc_admin','X-Demo-User':'poc.operador'}),...(options.headers||{})}});
  const body=await response.json().catch(()=>({}));
  if(!response.ok) throw new Error(body.detail||body.title||`HTTP ${response.status}`);
  return body;
};

function renderPersistenceState(p){
  const k=document.querySelector('#persistence-kpis');
  const list=document.querySelector('#persistence-list');
  const button=document.querySelector('#checkpoint');
  if(!k||!list||!button)return;
  k.innerHTML=[
    ['Provider',p.provider||'PostgreSQL'],
    ['Configurado',p.configured?'sim':'não'],
    ['Conexão',p.canConnect?'OK':'—'],
    ['Modo',p.mode||'—']
  ].map(([label,value])=>`<article><small>${label}</small><strong style="font-size:16px">${String(value)}</strong></article>`).join('');
  const migrations=Array.isArray(p.pendingMigrations)?p.pendingMigrations:[];
  list.innerHTML=`<article class="item"><div class="item-head"><div><h3>Fundação PostgreSQL</h3><p>${p.note||'DbContext, migration, tenant scope, checkpoint, outbox e idempotência.'}</p></div><span class="chip">${p.configured?'configurado':'fallback POC'}</span></div><div class="mono">migrations pendentes: ${migrations.length?migrations.join(', '):'nenhuma/indisponível'}</div></article>`;
  button.disabled=!p.configured;
  button.title=p.configured?'Cria snapshot transacional do estado demonstrativo':'Configure ConnectionStrings:Jundiai antes de criar checkpoint';
}

async function refreshPersistence(){
  try{renderPersistenceState(await persistenceApi('/api/audit/persistence/readiness'));}
  catch(e){const list=document.querySelector('#persistence-list');if(list)list.innerHTML=`<article class="item"><p>${e.message}</p></article>`;}
}

document.querySelector('#checkpoint')?.addEventListener('click',async()=>{
  try{
    const result=await persistenceApi('/api/audit/persistence/checkpoint',{method:'POST',body:JSON.stringify({label:'checkpoint-console-governanca'})});
    const list=document.querySelector('#persistence-list');
    if(list)list.insertAdjacentHTML('afterbegin',`<article class="item"><h3>Checkpoint criado</h3><p>${result.envelopeCount} envelopes · ${result.institutionId}</p><div class="mono">${result.checkpointId}</div></article>`);
  }catch(e){const list=document.querySelector('#persistence-list');if(list)list.insertAdjacentHTML('afterbegin',`<article class="item"><p>${e.message}</p></article>`);}
});

refreshPersistence();
