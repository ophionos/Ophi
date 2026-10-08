/**
 * What the service worker keeps for the read-only offline view, and when it throws it away. Pure
 * functions, so they are unit-tested here and imported by `src/service-worker.ts`.
 *
 * Saved pages and API reads hold one account's data. They live in their own caches (not the
 * per-version asset cache) and are deleted at every session boundary and on any 401, so a shared
 * device never shows the previous account's products.
 */

/** Rendered pages (navigations), for a reload while offline. */
export const PAGE_CACHE = 'ophi-pages';

/** API reads that the offline pages need. */
export const API_CACHE = 'ophi-api';

/** A saved copy older than this is never served. */
export const MAX_AGE_MS = 7 * 24 * 60 * 60 * 1000;

export const CACHED_AT_HEADER = 'x-ophi-cached-at';

/** Message type the worker posts to the page after each API read: saved copy or live answer. */
export const DATA_SOURCE_MESSAGE = 'ophi-data-source';

export interface DataSourceMessage {
	type: typeof DATA_SOURCE_MESSAGE;
	offline: boolean;
	/** Epoch ms of the saved copy, when `offline` is true. */
	cachedAt?: number;
}

const OFFLINE_API = [
	/^\/api\/v1\/auth\/me$/,
	/^\/api\/v1\/products$/,
	/^\/api\/v1\/products\/(?!export$)[^/]+$/,
	/^\/api\/v1\/products\/[^/]+\/history$/,
	/^\/api\/v1\/(tags|stores|comparisons)$/
];

const SESSION_BOUNDARIES = [
	['POST', /^\/api\/v1\/auth\/(login|logout|register)$/],
	['DELETE', /^\/api\/v1\/account$/]
] as const;

/**
 * The asset paths to precache, each once. `Cache.addAll` rejects a list with duplicate requests, and
 * that fails the whole install: `offline.html` is both a static file and the explicit fallback.
 */
export function precacheList(build: string[], files: string[], offlinePage: string): string[] {
	return [...new Set([...build, ...files, offlinePage])];
}

/** True for a same-origin API read that the dashboard or a product page needs offline. */
export function isOfflineApi(url: URL, origin: string): boolean {
	return url.origin === origin && OFFLINE_API.some((pattern) => pattern.test(url.pathname));
}

/** True for a request after which the saved data may belong to someone else. */
export function endsSession(method: string, url: URL, origin: string): boolean {
	return (
		url.origin === origin &&
		SESSION_BOUNDARIES.some(([m, pattern]) => m === method && pattern.test(url.pathname))
	);
}

/** A copy of `response` that carries the time it was saved. */
export async function stamp(response: Response, now: number): Promise<Response> {
	const headers = new Headers(response.headers);
	headers.set(CACHED_AT_HEADER, String(now));
	return new Response(await response.arrayBuffer(), {
		status: response.status,
		statusText: response.statusText,
		headers
	});
}

/** The time a stamped copy was saved, or null when it has no stamp. */
export function cachedAt(response: Response): number | null {
	const value = Number(response.headers.get(CACHED_AT_HEADER));
	return Number.isFinite(value) && value > 0 ? value : null;
}

/** True when the copy has a stamp that is at most {@link MAX_AGE_MS} old. */
export function isFresh(response: Response, now: number): boolean {
	const saved = response.headers.get(CACHED_AT_HEADER);
	return saved !== null && now - Number(saved) <= MAX_AGE_MS;
}
