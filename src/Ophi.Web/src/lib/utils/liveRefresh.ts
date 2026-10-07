import { liveUpdates, type LiveUpdateEvent } from '$lib/stores/liveUpdates.svelte';

export interface LiveRefreshOptions {
	/** Which pushed SSE events should trigger a refresh. */
	matches: (evt: LiveUpdateEvent) => boolean;
	/** The refetch to run on a push or fallback poll tick. */
	refresh: () => void | Promise<void>;
	/** Poll only while this is true (and SSE is down). Default: always. */
	shouldPoll?: () => boolean;
	/** Fallback poll cadence. Default 3000ms. */
	intervalMs?: number;
	/** Optional cap on fallback polls so a dead target can't poll forever. */
	maxPolls?: number;
}

/**
 * The standard "live surface" wiring in one place: refresh on matching SSE pushes, and fall back
 * to polling only while the SSE connection is down. Call from inside a `$effect` and return its
 * result as the cleanup — reads of `liveUpdates.connected` and `shouldPoll()` happen during the
 * effect run, so the effect re-wires itself when either changes:
 *
 * ```ts
 * $effect(() => liveRefresh({ matches: (e) => e.type === 'scrape-completed', refresh }));
 * ```
 */
export function liveRefresh(opts: LiveRefreshOptions): () => void {
	const unsubscribe = liveUpdates.subscribe((evt) => {
		if (opts.matches(evt)) opts.refresh();
	});

	let interval: ReturnType<typeof setInterval> | undefined;
	if (!liveUpdates.connected && (opts.shouldPoll?.() ?? true)) {
		let polls = 0;
		interval = setInterval(() => {
			polls += 1;
			if (opts.maxPolls !== undefined && polls > opts.maxPolls) {
				clearInterval(interval);
				return;
			}
			opts.refresh();
		}, opts.intervalMs ?? 3000);
	}

	return () => {
		unsubscribe();
		if (interval) clearInterval(interval);
	};
}
