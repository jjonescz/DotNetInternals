// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.ttf$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await cache.addAll(assetsRequests);
    
    // Clean responses.
    // Removes `redirected` flag so the response is servable by the service worker.
    // https://stackoverflow.com/a/45440505/9080566
    // https://github.com/dotnet/aspnetcore/issues/33872
    // Also avoids other inexplicable failures when serving responses from the service worker.
    for (const request of assetsRequests) {
        const response = await cache.match(request);
        const clonedResponse = response.clone();
        const responseData = await clonedResponse.arrayBuffer();
        const cleanedResponse = new Response(responseData, {
            headers: {
                'content-type': clonedResponse.headers.get('content-type') ?? '',
                'content-length': responseData.byteLength.toString(),
            },
        });
        await cache.put(request, cleanedResponse);
    }
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        if (shouldServeIndexHtml) {
            console.debug(`Service worker: serving index.html for ${event.request.url}`);
        }

        const request = shouldServeIndexHtml ? 'index.html' : event.request;

        const cache = await caches.open(cacheName);
        // We ignore search query (so our pre-cached `app.css` matches request `app.css?v=2`),
        // we have pre-cached the latest versions of all static assets.
        cachedResponse = await cache.match(request, { ignoreSearch: true });
    }

    if (!cachedResponse) {
        console.debug(`Service worker: cache miss for ${event.request.url}`);
    }

    return cachedResponse || fetch(event.request);
}
