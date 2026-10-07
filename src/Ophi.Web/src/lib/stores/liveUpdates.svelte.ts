import { browser } from '$app/environment';
import { API_BASE } from '$lib/api/client';

/**
 * A thin "something changed, refetch" ping pushed over the SSE stream. Carries no price data —
 * each surface decides what to refetch from its own loader. Mirrors the backend `LiveUpdate.Kind`.
 */
export interface LiveUpdateEvent {
	type: 'scrape-completed' | 'notification' | (string & {});
	productId?: string | null;
}

type Listener = (event: LiveUpdateEvent) => void;

let source: EventSource | null = null;
let connected = $state(false);
// eslint-disable-next-line svelte/prefer-svelte-reactivity -- listener registry, never rendered
const listeners = new Set<Listener>();

function handleMessage(e: MessageEvent) {
	try {
		const event = JSON.parse(e.data) as LiveUpdateEvent;
		for (const listener of listeners) listener(event);
	} catch {
		// Ignore malformed frames. Heartbeat comments (`: ping`) never arrive as messages, so the
		// only thing reaching here is a real data frame; a parse failure means something unexpected.
	}
}

/**
 * App-wide Server-Sent-Events client: one `EventSource` shared across every surface. Mounted once in
 * the root layout while a user is signed in. Surfaces call {@link subscribe} to react to pushes;
 * existing per-surface polls fall back on `!connected` so the app still updates if SSE can't connect.
 */
export const liveUpdates = {
	/** True between `onopen` and the next `onerror`. EventSource auto-reconnects; this drives fallbacks. */
	get connected() {
		return connected;
	},
	connect() {
		if (!browser || source) return;
		// API_BASE already includes the `/api/v1` prefix, so the path here is just `/events`.
		// (Prefixing `/api/v1` again produced `/api/v1/api/v1/events` → 404, silently killing SSE.)
		source = new EventSource(`${API_BASE}/events`, { withCredentials: true });
		source.onopen = () => {
			connected = true;
		};
		source.onerror = () => {
			// The browser will retry automatically; surfaces poll while we're marked disconnected.
			connected = false;
		};
		source.onmessage = handleMessage;
	},
	disconnect() {
		source?.close();
		source = null;
		connected = false;
	},
	/** Register a listener; returns an unsubscribe to call from effect/onMount cleanup. */
	subscribe(listener: Listener): () => void {
		listeners.add(listener);
		return () => {
			listeners.delete(listener);
		};
	}
};
