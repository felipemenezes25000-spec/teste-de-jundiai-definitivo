const CACHE='jundiai-healthos-poc-v13';
const ASSETS=['/','/index.html','/styles.css','/clinical.css','/app.js','/citizen.html','/operations.html','/esus.html','/acs.html','/manifest.webmanifest','/login.html','/poc.html','/poc.css','/poc.js','/evidence-pack.html','/evidence-pack.js','/dossier.html','/dossier.js','/caretrace.html','/caretrace.js','/registration.html','/registration.js','/workforce.html','/workforce.js','/referrals.html','/referrals.js','/clinical-ops.html','/clinical-ops.js','/agenda.html','/agenda.js','/telemedicine.html','/telemedicine-ui.js','/immunization-v2.html','/immunization-v2.js','/pharmacy-care.html','/pharmacy-care.js','/command-center.html','/command-center.js','/verification.html','/verification.js','/diagnostics.html','/diagnostics.js','/dental-v2.html','/dental-v2.js','/governance.html','/governance.js','/governance-persistence.js','/governance-privacy.js','/billing-v2.html','/billing-v2.js'];
self.addEventListener('install',event=>event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(ASSETS))));
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(k=>k!==CACHE).map(k=>caches.delete(k))))));
self.addEventListener('fetch',event=>{
  const request=event.request;
  if(request.method!=='GET')return;
  if(new URL(request.url).pathname.startsWith('/api/')){
    event.respondWith(fetch(request).catch(()=>new Response(JSON.stringify({offline:true}),{status:503,headers:{'Content-Type':'application/json'}})));
    return;
  }
  event.respondWith(caches.match(request).then(cached=>cached||fetch(request).then(response=>{const copy=response.clone();caches.open(CACHE).then(cache=>cache.put(request,copy));return response;})));
});