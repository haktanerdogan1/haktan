/**
 * VELİEFENDİ & PADOK — Servis Çalışanı
 * - Uygulama kabuğu (HTML/JS/ikonlar) önce ağdan gelir; ağ yoksa son kopyadan açılır.
 * - Canlı TJK verisi (/api/*) her zaman ağdan gelir; ağ yoksa son başarılı bülten gösterilir.
 * - Tailwind ve Google Fonts gibi CDN dosyaları çevrimdışı kullanım için saklanır.
 */
const VERSION = 'padok-v4';
const SHELL = ['/', '/index.html', '/app.js', '/manifest.json', '/icon.svg', '/icon-192.png', '/icon-512.png'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(VERSION).then(c => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k !== VERSION).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', event => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);

  // Canlı veri: her zaman ağdan. Çevrimdışı kullanım için yalnızca bülten saklanır
  // (safkan karnesi/idman yanıtlarını saklamak tarayıcı önbelleğini sınırsız büyütürdü).
  if (url.origin === location.origin && url.pathname.startsWith('/api/')) {
    const keep = url.pathname === '/api/program';
    // Önbellek anahtarı: refresh parametresi olmadan (↻ ile çekilen bülten de aynı kaydı günceller)
    const key = new URL(url); key.searchParams.delete('refresh');
    event.respondWith(
      fetch(req).then(res => {
        if (res.ok && keep) {
          const copy = res.clone();
          caches.open(VERSION).then(c => c.put(key.toString(), copy));
        }
        return res;
      }).catch(() => caches.match(keep ? key.toString() : req).then(hit => hit || new Response(
        JSON.stringify({ ok: false, error: 'Çevrimdışısınız ve bu bülten daha önce kaydedilmedi.' }),
        { status: 503, headers: { 'Content-Type': 'application/json; charset=utf-8' } }
      )))
    );
    return;
  }

  // Kendi dosyalarımız: önce ağ (her zaman güncel kod), ağ yoksa önbellek
  if (url.origin === location.origin) {
    event.respondWith(
      fetch(req).then(res => {
        if (res.ok) {
          const copy = res.clone();
          caches.open(VERSION).then(c => c.put(req, copy));
        }
        return res;
      }).catch(() => caches.match(req).then(hit => hit || caches.match('/index.html')))
    );
    return;
  }

  // CDN dosyaları (Tailwind, Google Fonts): önbellekten ver, arka planda tazele
  if (!/(^|\.)(cdn\.tailwindcss\.com|fonts\.googleapis\.com|fonts\.gstatic\.com)$/.test(url.hostname)) return;
  event.respondWith(
    caches.open(VERSION).then(cache => cache.match(req).then(hit => {
      const network = fetch(req).then(res => {
        if (res && (res.ok || res.type === 'opaque')) cache.put(req, res.clone());
        return res;
      }).catch(() => hit);
      return hit || network;
    }))
  );
});
