const CACHE='jundiai-healthos-poc-v5';
const ASSETS=['/','/index.html','/styles.css','/clinical.css','/app.js','/citizen.html','/operations.html','/esus.html','/acs.html','/manifest.webmanifest','/login.html','/poc.html','/poc.css','/poc.js','/caretrace.html','/caretrace.js','/agenda.html','/agenda.js','/diagnostics.html','/diagnostics.js','/dental-v2.html','/dental-v2.js','/governance.html','/governance.js','/billing-v2.html','/billing-v2.js'];
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
