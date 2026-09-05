// ============================================================================================
// PoSeeReview service worker
//
// NETWORK-FIRST, DELIBERATELY. Read this before switching anything to cache-first.
//
// Blazor WebAssembly verifies every framework file against the integrity hashes in
// blazor.boot.json. Those files are NOT fingerprinted by filename, so a cache-first worker that
// serves yesterday's `_framework/*.wasm` alongside today's boot manifest produces an integrity
// failure and a white screen — with no way for the user to recover except clearing site data.
// Network-first means the network decides what is current and the cache only ever answers when
// the network could not, which is the one behaviour that cannot break a deploy.
//
// Everything here is scoped to same-origin GETs. Comic images live on Blob Storage (a different
// origin, no CORS headers), so those responses would be opaque — cacheable only as blobs of
// unknown status, taking real quota for something that cannot be inspected. They pass straight
// through.
// ============================================================================================

const VERSION = 'v1';
const SHELL_CACHE = `posee-shell-${VERSION}`;
const RUNTIME_CACHE = `posee-runtime-${VERSION}`;

// The smallest set that makes an offline launch show this app rather than the browser's error
// page. Everything else is cached opportunistically as it is fetched.
const SHELL_ASSETS = [
    '/',
    '/offline.html',
    '/css/app.css',
    '/icons/icon-192.png',
    '/manifest.webmanifest'
];

self.addEventListener('install', event => {
    event.waitUntil((async () => {
        const cache = await caches.open(SHELL_CACHE);
        // addAll is all-or-nothing; one 404 would abort the whole install and leave the app with
        // no worker at all. Each asset is added independently so a missing one is survivable.
        await Promise.all(SHELL_ASSETS.map(async asset => {
            try {
                await cache.add(new Request(asset, { cache: 'reload' }));
            } catch {
                // Not fatal — this asset simply will not be available offline.
            }
        }));
        await self.skipWaiting();
    })());
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys
            .filter(key => key.startsWith('posee-') && key !== SHELL_CACHE && key !== RUNTIME_CACHE)
            .map(key => caches.delete(key)));
        await self.clients.claim();
    })());
});

/**
 * Paths that must never be served from a cache.
 *
 * `/api` and `/auth` are obvious. `/diag` is included because a cached diagnostics snapshot is
 * actively misleading — it is the page someone opens precisely when they need to know the
 * current state of the system.
 */
function isNeverCached(url) {
    return url.pathname.startsWith('/api/')
        || url.pathname.startsWith('/auth/')
        || url.pathname.startsWith('/diag')
        || url.pathname.startsWith('/health');
}

/** Static assets worth keeping a copy of for offline use. */
function isCacheableAsset(url) {
    return /\.(?:css|js|wasm|dat|json|png|jpg|jpeg|svg|webp|woff2?|ttf)$/i.test(url.pathname)
        || url.pathname.startsWith('/_framework/')
        || url.pathname.startsWith('/_content/');
}

self.addEventListener('fetch', event => {
    const request = event.request;

    if (request.method !== 'GET') {
        return;
    }

    const url = new URL(request.url);

    // Cross-origin (comic blobs, fonts from a CDN) is left entirely alone.
    if (url.origin !== self.location.origin) {
        return;
    }

    if (isNeverCached(url)) {
        return;
    }

    // A navigation is the one request where an offline answer is much better than an error: the
    // SPA shell can boot and show the app's own "you are offline" state.
    if (request.mode === 'navigate') {
        event.respondWith(handleNavigation(request));
        return;
    }

    if (isCacheableAsset(url)) {
        event.respondWith(networkFirst(request, RUNTIME_CACHE));
    }
});

async function handleNavigation(request) {
    try {
        const response = await fetch(request);

        // Only a real 200 is worth keeping. Caching a redirect or an error page as the shell is
        // how an app gets stuck showing its own 500.
        if (response.ok && response.type === 'basic') {
            const cache = await caches.open(SHELL_CACHE);
            cache.put('/', response.clone());
        }

        return response;
    } catch {
        return (await caches.match('/'))
            ?? (await caches.match('/offline.html'))
            ?? Response.error();
    }
}

async function networkFirst(request, cacheName) {
    try {
        const response = await fetch(request);

        if (response.ok) {
            const cache = await caches.open(cacheName);
            cache.put(request, response.clone());
        }

        return response;
    } catch (error) {
        const cached = await caches.match(request);
        if (cached) {
            return cached;
        }
        throw error;
    }
}

// Lets the page trigger an immediate update instead of waiting for every tab to close.
self.addEventListener('message', event => {
    if (event.data === 'skipWaiting') {
        self.skipWaiting();
    }
});
