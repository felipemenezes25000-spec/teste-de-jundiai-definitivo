const packToken=()=>localStorage.getItem('jundiai.session');
const packHeaders=(extra={})=>({'Content-Type':'application/json',...(packToken()?{Authorization:`Bearer ${packToken()}`}:{}),...extra});
const packEsc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function packApi(url,options={}){
  const response=await fetch(url,{...options,headers:packHeaders(options.headers||{})});
  const body=await response.json().catch(()=>({}));
  if(!response.ok) throw new Error(body.detail||body.title||`HTTP ${response.status}`);
  return body;
}

function setPackError(message){
  const box=document.querySelector('#error');
  if(!box)return;
  box.hidden=!message;
  box.textContent=message||'';
}

function renderPack(pack){
  const payload=pack.payload;
  const verification=payload.verification;
  const score=verification.overallScore||0;
  document.querySelector('#score-value').textContent=score;
  document.querySelector('#pack-score').style.setProperty('--score',`${score}%`);

  const summary=document.querySelector('#summary');
  summary.innerHTML=[
    ['Blocos aprovados',`${verification.passedBlocks}/${verification.totalBlocks}`],
    ['Score POC',score],
    ['Integrações',payload.integrations?.length||0],
    ['Eventos capturados',payload.evidenceEvents?.length||0]
  ].map(([label,value])=>`<article><small>${packEsc(label)}</small><strong>${packEsc(value)}</strong></article>`).join('');

  document.querySelector('#pack-meta').innerHTML=`<strong>Evidence Pack ${packEsc(payload.version)}</strong><p>Gerado em ${packEsc(new Date(payload.generatedAt).toLocaleString('pt-BR'))} · instituição ${packEsc(payload.institutionId)}${payload.healthUnitId?` · unidade ${packEsc(payload.healthUnitId)}`:''}</p><div class="hash">SHA-256 ${packEsc(pack.packageSha256)}<br/>canonicalização ${packEsc(pack.canonicalization)}</div>`;

  document.querySelector('#blocks').innerHTML=(payload.blocks||[]).map(block=>{
    const ledger=(block.ledgerEvidence||[]).slice(-3);
    return `<article class="pack-block"><header><div><small>Bloco ${block.block}</small><h3>${packEsc(block.name)}</h3></div><span class="pill ${block.passed?'ok':'warn'}">${block.passed?'APROVADO':'ATENÇÃO'} · ${block.score}</span></header><p>${packEsc(block.evidence)}</p><div class="routes">UI ${packEsc(block.uiRoute)}<br/>${(block.evidenceEndpoints||[]).map(packEsc).join(' · ')}</div><p><strong>Capacidades:</strong> ${(block.capabilities||[]).map(packEsc).join(' · ')}</p>${ledger.length?`<div class="mini-list">${ledger.map(item=>`<div class="mini"><small>#${item.sequence} · ${packEsc(item.action)}</small><strong>${packEsc(item.resource)}</strong><small>${packEsc(item.hash)}</small></div>`).join('')}</div>`:'<small>Sem eventos específicos na janela capturada; as rotas do bloco permanecem indexadas.</small>'}</article>`;
  }).join('');

  document.querySelector('#integrations').innerHTML=(payload.integrations||[]).map(item=>`<article class="module"><div class="row"><strong>${packEsc(item.name)}</strong><span class="pill">${packEsc(item.status)}</span></div><p>${packEsc(item.domain)} · ambiente ${packEsc(item.environment)}</p><small>${packEsc(item.note)}</small></article>`).join('');
  document.querySelector('#blockers').innerHTML=(payload.nonCodeBlockers||[]).map(item=>`<article class="mini"><strong>${packEsc(item.id)} · ${packEsc(item.owner)}</strong><small>${packEsc(item.severity)}</small><p>${packEsc(item.description)}</p></article>`).join('');

  const p=payload.persistence||{};
  const db=p.database||{}; const recovery=p.recovery||{}; const messaging=p.messaging||{};
  document.querySelector('#persistence').innerHTML=`
    <article class="module"><h3>Database</h3><p>${p.configured?'PostgreSQL configurado':'fallback da POC'}</p><div class="rows"><div class="row"><span>modo</span><strong>${packEsc(db.mode||p.mode||'—')}</strong></div><div class="row"><span>conexão</span><strong>${db.canConnect?'OK':'—'}</strong></div></div></article>
    <article class="module"><h3>Recovery</h3><p>checkpoint + manifesto + drill</p><div class="rows"><div class="row"><span>checkpoints</span><strong>${packEsc(recovery.checkpoints??0)}</strong></div><div class="row"><span>drill</span><strong>${recovery.recoveryDrillAvailable?'disponível':'—'}</strong></div></div></article>
    <article class="module"><h3>Messaging</h3><p>inbox/outbox persistentes</p><div class="rows"><div class="row"><span>inbox receipts</span><strong>${packEsc(messaging.inboxReceipts??0)}</strong></div><div class="row"><span>outbox pendente</span><strong>${packEsc(messaging.pendingOutbox??0)}</strong></div><div class="row"><span>dead-letter</span><strong>${packEsc(messaging.deadLetter??0)}</strong></div></div></article>`;
}

async function loadLatest(){
  try{
    const response=await fetch('/api/poc/evidence-pack/latest',{headers:packHeaders()});
    if(response.status===404)return;
    const body=await response.json().catch(()=>({}));
    if(!response.ok) throw new Error(body.detail||body.title||`HTTP ${response.status}`);
    renderPack(body);
  }catch(error){setPackError(error.message);}
}

document.querySelector('#generate')?.addEventListener('click',async()=>{
  const button=document.querySelector('#generate');
  try{
    setPackError(''); button.disabled=true; button.textContent='Gerando…';
    const pack=await packApi('/api/poc/evidence-pack',{method:'POST',body:JSON.stringify({actor:'console.evidence-pack',reRunVerification:true})});
    renderPack(pack);
  }catch(error){setPackError(error.message);}finally{button.disabled=false;button.textContent='Gerar Evidence Pack';}
});

document.querySelector('#verify')?.addEventListener('click',async()=>{
  try{
    setPackError('');
    const result=await packApi('/api/poc/evidence-pack/latest/verify');
    const ok=result.demonstrationIntegrityReady;
    const meta=document.querySelector('#pack-meta');
    meta.insertAdjacentHTML('afterbegin',`<div class="risk" style="border-color:${ok?'#a8d5b6':'#efc06b'}"><strong>${ok?'INTEGRIDADE APROVADA':'ATENÇÃO NA INTEGRIDADE'}</strong><p>package hash ${result.packageHashValid?'OK':'FALHA'} · Evidence Ledger ${result.ledgerChainValid?'OK':'FALHA'} · blocos ${result.passedBlocks}/${result.totalBlocks}</p></div>`);
  }catch(error){setPackError(error.message);}
});

document.querySelector('#export')?.addEventListener('click',async event=>{
  event.preventDefault();
  try{
    setPackError('');
    const response=await fetch('/api/poc/evidence-pack/latest/export',{headers:packHeaders({'Accept':'application/json'})});
    if(!response.ok){const body=await response.json().catch(()=>({}));throw new Error(body.detail||body.title||`HTTP ${response.status}`);}
    const blob=await response.blob();
    const disposition=response.headers.get('Content-Disposition')||'';
    const match=disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
    const name=match?decodeURIComponent(match[1].replace(/\"/g,'')):'jundiai-evidence-pack.json';
    const url=URL.createObjectURL(blob); const link=document.createElement('a'); link.href=url; link.download=name; document.body.appendChild(link); link.click(); link.remove(); URL.revokeObjectURL(url);
  }catch(error){setPackError(error.message);}
});

loadLatest();