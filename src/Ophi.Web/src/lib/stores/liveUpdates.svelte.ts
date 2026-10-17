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

// Reconnect backoff: each consecutive failure doubles the step, capped; a successful open resets it.
// Without this, EventSource's built-in retry hits a down server every few seconds from every tab.
const BASE_RECONNECT_MS = 2000;
const MAX_RECONNECT_MS = 60_000;

let source: EventSource | null = null;
let connected = $state(false);
let active = false; // between connect() and disconnect()
let failures = 0;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
let awaitingVisible = false;
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

function open() {
	// API_BASE already includes the `/api/v1` prefix, so the path here is just `/events`.
	// (Prefixing `/api/v1` again produced `/api/v1/api/v1/events` → 404, silently killing SSE.)
	source = new EventSource(`${API_BASE}/events`, { withCredentials: true });
	source.onopen = () => {
		connected = true;
		failures = 0;
	};
	source.onerror = () => {
		// Close instead of letting the browser retry at its fixed rate, and reconnect on our own
		// backoff. Surfaces poll while we're marked disconnected.
		connected = false;
		source?.close();
		source = null;
		scheduleReconnect();
	};
	source.onmessage = handleMessage;
}

function scheduleReconnect() {
	const step = Math.min(MAX_RECONNECT_MS, BASE_RECONNECT_MS * 2 ** failures);
	failures++;
	// Jitter within the upper half of the step, so tabs that lost the server together spread out.
	const delay = step / 2 + (Math.random() * step) / 2;
	reconnectTimer = setTimeout(() => {
		reconnectTimer = null;
		// A hidden tab doesn't need live updates; reconnect once it is visible again.
		if (document.hidden) {
			awaitingVisible = true;
			return;
		}
		open();
	}, delay);
}

function handleVisibilityChange() {
	if (awaitingVisible && !document.hidden) {
		awaitingVisible = false;
		open();
	}
}

/**
 * App-wide Server-Sent-Events client: one `EventSource` shared across every surface. Mounted once in
 * the root layout while a user is signed in. Surfaces call {@link subscribe} to react to pushes;
 * existing per-surface polls fall back on `!connected` so the app still updates if SSE can't connect.
 */
export const liveUpdates = {
	/** True between `onopen` and the next `onerror`. Reconnects use a backoff; this drives fallbacks. */
	get connected() {
		return connected;
	},
	connect() {
		if (!browser || active) return;
		active = true;
		document.addEventListener('visibilitychange', handleVisibilityChange);
		open();
	},
	disconnect() {
		active = false;
		document.removeEventListener('visibilitychange', handleVisibilityChange);
		if (reconnectTimer) clearTimeout(reconnectTimer);
		reconnectTimer = null;
		awaitingVisible = false;
		failures = 0;
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
