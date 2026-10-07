import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import type { LiveUpdateEvent } from '$lib/stores/liveUpdates.svelte';

// Controllable fake of the SSE store: tests flip `connected` and emit events directly.
const listeners = new Set<(evt: LiveUpdateEvent) => void>();
let connected = false;

vi.mock('$lib/stores/liveUpdates.svelte', () => ({
	liveUpdates: {
		get connected() {
			return connected;
		},
		subscribe(listener: (evt: LiveUpdateEvent) => void) {
			listeners.add(listener);
			return () => listeners.delete(listener);
		}
	}
}));

function emit(evt: LiveUpdateEvent) {
	for (const l of listeners) l(evt);
}

import { liveRefresh } from './liveRefresh';

describe('liveRefresh', () => {
	beforeEach(() => {
		listeners.clear();
		connected = false;
		vi.useFakeTimers();
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('should refresh when a matching event is pushed', () => {
		const refresh = vi.fn();
		const cleanup = liveRefresh({
			matches: (evt) => evt.type === 'scrape-completed',
			refresh
		});

		emit({ type: 'scrape-completed' });
		emit({ type: 'notification' });

		expect(refresh).toHaveBeenCalledTimes(1);
		cleanup();
	});

	it('should poll while disconnected', () => {
		const refresh = vi.fn();
		const cleanup = liveRefresh({
			matches: () => false,
			refresh,
			intervalMs: 1000
		});

		vi.advanceTimersByTime(3000);

		expect(refresh).toHaveBeenCalledTimes(3);
		cleanup();
	});

	it('should not poll while connected', () => {
		connected = true;
		const refresh = vi.fn();
		const cleanup = liveRefresh({
			matches: () => false,
			refresh,
			intervalMs: 1000
		});

		vi.advanceTimersByTime(5000);

		expect(refresh).not.toHaveBeenCalled();
		cleanup();
	});

	it('should not poll when shouldPoll returns false', () => {
		const refresh = vi.fn();
		const cleanup = liveRefresh({
			matches: () => false,
			refresh,
			shouldPoll: () => false,
			intervalMs: 1000
		});

		vi.advanceTimersByTime(5000);

		expect(refresh).not.toHaveBeenCalled();
		cleanup();
	});

	it('should stop polling after maxPolls', () => {
		const refresh = vi.fn();
		const cleanup = liveRefresh({
			matches: () => false,
			refresh,
			intervalMs: 1000,
			maxPolls: 2
		});

		vi.advanceTimersByTime(10000);

		expect(refresh).toHaveBeenCalledTimes(2);
		cleanup();
	});

	it('should unsubscribe and stop polling on cleanup', () => {
		const refresh = vi.fn();
		const cleanup = liveRefresh({
			matches: () => true,
			refresh,
			intervalMs: 1000
		});

		cleanup();
		emit({ type: 'scrape-completed' });
		vi.advanceTimersByTime(5000);

		expect(refresh).not.toHaveBeenCalled();
	});
});
