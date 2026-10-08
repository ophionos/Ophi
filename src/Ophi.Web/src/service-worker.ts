/// <reference types="@sveltejs/kit" />
/// <reference no-default-lib="true"/>
/// <reference lib="esnext" />
/// <reference lib="webworker" />

import { build, files, version } from '$service-worker';
import {
	API_CACHE,
	DATA_SOURCE_MESSAGE,
	PAGE_CACHE,
	cachedAt,
	endsSession,
	isFresh,
	isOfflineApi,
	precacheList,
	stamp,
	type DataSourceMessage
} from '$lib/offline/cache-policy';

const sw = self as unknown as ServiceWorkerGlobalScope;
const CACHE = `cache-${version}`;
const OFFLINE_PAGE = '/offline.html';
const ASSETS = precacheList(build, files, OFFLINE_PAGE);

sw.addEventListener('install', (event) => {
	event.waitUntil(
		caches
			.open(CACHE)
			.then((cache) => cache.addAll(ASSETS))
			.then(() => sw.skipWaiting())
	);
});

sw.addEventListener('activate', (event) => {
	event.waitUntil(
		caches.keys().then(async (keys) => {
			for (const key of keys) {
				// The account caches survive an app update; only old asset caches go.
				if (key !== CACHE && key !== PAGE_CACHE && key !== API_CACHE) await caches.delete(key);
			}
			sw.clients.claim();
		})
	);
});

/** Deletes every saved page and API read: they belong to the account that was signed in. */
function clearAccountData() {
	return Promise.all([caches.delete(PAGE_CACHE), caches.delete(API_CACHE)]);
}

async function notify(clientId: string, message: DataSourceMessage) {
	const client = clientId ? await sw.clients.get(clientId) : undefined;
	client?.postMessage(message);
}

/** A fresh saved copy of the request, or undefined. A stale copy is deleted. */
async function savedCopy(cacheName: string, request: Request): Promise<Response | undefined> {
	const cache = await caches.open(cacheName);
	const saved = await cache.match(request);
	if (!saved) return undefined;
	if (isFresh(saved, Date.now())) return saved;
	await cache.delete(request);
	return undefined;
}

/** Network first. A 200 is saved with a time stamp; a network failure falls back to a fresh copy. */
async function networkFirst(event: FetchEvent, cacheName: string): Promise<Response> {
	try {
		const response = await fetch(event.request);
		if (response.status === 401) {
			await clearAccountData();
		} else if (response.status === 200) {
			const copy = await stamp(response.clone(), Date.now());
			await caches.open(cacheName).then((cache) => cache.put(event.request, copy));
		}
		if (cacheName === API_CACHE) {
			void notify(event.clientId, { type: DATA_SOURCE_MESSAGE, offline: false });
		}
		return response;
	} catch (error) {
		const saved = await savedCopy(cacheName, event.request);
		if (cacheName === API_CACHE && saved) {
			void notify(event.clientId, {
				type: DATA_SOURCE_MESSAGE,
				offline: true,
				cachedAt: cachedAt(saved) ?? undefined
			});
		}
		if (saved) return saved;
		if (event.request.mode === 'navigate') {
			const offline = await caches.match(OFFLINE_PAGE);
			if (offline) return offline;
		}
		throw error;
	}
}

sw.addEventListener('fetch', (event) => {
	const url = new URL(event.request.url);
	const origin = sw.location.origin;

	if (event.request.method !== 'GET') {
		// Login, logout, register and account deletion: the saved data may now be someone else's.
		if (endsSession(event.request.method, url, origin)) event.waitUntil(clearAccountData());
		return;
	}

	// Cache-first for static assets
	if (url.origin === origin && ASSETS.includes(url.pathname)) {
		event.respondWith(
			caches.match(event.request).then((cached) => cached || fetch(event.request))
		);
		return;
	}

	// The API reads the offline pages need; every other API call goes straight to the network.
	if (url.pathname.startsWith('/api')) {
		if (isOfflineApi(url, origin)) event.respondWith(networkFirst(event, API_CACHE));
		return;
	}

	// Pages: network first, with a saved copy for a reload while offline.
	if (event.request.mode === 'navigate' && url.origin === origin) {
		event.respondWith(networkFirst(event, PAGE_CACHE));
	}
});
