const governanceExtraApi=async(url,options={})=>{const token=localStorage.getItem('jundiai.session');const r=await fetch(url,{...options,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{'X-Demo-Role':'poc_admin','X-Demo-User':'poc.operador'}),...(options.headers||{})}});const b=await r.json().catch(()=>({}));if(!r.ok)throw new Error(b.detail||b.title||`HTTP ${r.status}`);return b;};
const extraEsc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function loadPrivacy(){
  try{
    const [readiness,policies,grants,citizens]=await Promise.all([
      governanceExtraApi('/api/audit/privacy/readiness'),
      governanceExtraApi('/api/audit/privacy/policies'),
      governanceExtraApi('/api/audit/privacy/break-glass'),
      governanceExtraApi('/api/citizens')
    ]);
    const k=document.querySelector('#privacy-kpis');
    const list=document.querySelector('#privacy-list');
    if(k)k.innerHTML=[['Políticas',readiness.policies],['Break-glass ativo',readiness.breakGlass.active],['Revogados',readiness.breakGlass.revoked],['Gates prod.',readiness.productionGates.length]].map(x=>`<article><small>${x[0]}</small><strong>${x[1]}</strong></article>`).join('');
    if(list)list.innerHTML=policies.map(p=>`<article class="item"><div class="item-head"><div><h3>${extraEsc(p.id)} · ${extraEsc(p.name)}</h3><p>${extraEsc(p.rule)}</p></div><span class="chip">policy</span></div></article>`).join('')+grants.slice(0,6).map(g=>`<article class="item"><div class="item-head"><div><h3>Break-glass · ${extraEsc(g.actor)}</h3><p>${extraEsc(g.reason)} · expira ${new Date(g.expiresAt).toLocaleString('pt-BR')}</p></div><span class="chip">${extraEsc(g.status)}</span></div></article>`).join('');
    const select=document.querySelector('#privacy-citizen');
    if(select)select.innerHTML=citizens.map(c=>`<option value="${c.id}">${extraEsc(c.name)}</option>`).join('');
  }catch(e){const list=document.querySelector('#privacy-list');if(list)list.innerHTML=`<article class="item"><p>${extraEsc(e.message)}</p></article>`;}
}

async function loadTelemetry(){
  try{
    const t=await governanceExtraApi('/api/operations/telemetry');
    const k=document.querySelector('#telemetry-kpis');
    const list=document.querySelector('#telemetry-list');
    if(k)k.innerHTML=[['Requests',t.totalRequests],['5xx',t.serverErrors],['Erro %',t.errorRate],['Grupos',t.groups.length]].map(x=>`<article><small>${x[0]}</small><strong>${x[1]}</strong></article>`).join('');
    if(list)list.innerHTML=t.groups.map(g=>`<article class="item"><div class="item-head"><div><h3>${extraEsc(g.key)}</h3><p>${g.count} req · média ${g.averageMs} ms · máx. ${g.maxMs} ms</p></div><span class="chip">${g.errors} erro(s)</span></div></article>`).join('');
  }catch(e){const list=document.querySelector('#telemetry-list');if(list)list.innerHTML=`<article class="item"><p>${extraEsc(e.message)}</p></article>`;}
}

document.querySelector('#privacy-breakglass')?.addEventListener('click',async()=>{
  const citizenId=document.querySelector('#privacy-citizen')?.value;
  if(!citizenId)return;
  try{
    await governanceExtraApi('/api/audit/privacy/break-glass',{method:'POST',body:JSON.stringify({citizenId,actor:'admin.jundiai',reason:'Demonstração controlada do mecanismo break-glass',minutes:10})});
    await loadPrivacy();
  }catch(e){alert(e.message);}
});

document.querySelector('#privacy-export')?.addEventListener('click',async()=>{
  const citizenId=document.querySelector('#privacy-citizen')?.value;
  if(!citizenId)return;
  try{
    const result=await governanceExtraApi('/api/audit/privacy/subject-export',{method:'POST',body:JSON.stringify({citizenId,actor:'admin.jundiai',purpose:'Demonstração do direito do titular'})});
    const output=document.querySelector('#privacy-export-result');
    if(output)output.innerHTML=`<div class="mono">export ${extraEsc(result.id)}<br>SHA-256 ${extraEsc(result.sha256)}<br>${extraEsc(result.contentType)}</div>`;
  }catch(e){alert(e.message);}
});

document.querySelector('#telemetry-refresh')?.addEventListener('click',loadTelemetry);
loadPrivacy();loadTelemetry();
