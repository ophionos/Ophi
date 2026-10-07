import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { liveUpdates, type LiveUpdateEvent } from './liveUpdates.svelte';
import { API_BASE } from '$lib/api/client';

// Minimal EventSource stand-in: jsdom doesn't implement EventSource, and we want to drive
// open/message/error from the test rather than open a real network connection.
class MockEventSource {
	static last: MockEventSource | null = null;
	url: string;
	withCredentials: boolean;
	onopen: (() => void) | null = null;
	onerror: (() => void) | null = null;
	onmessage: ((e: { data: string }) => void) | null = null;
	closed = false;

	constructor(url: string, init?: { withCredentials?: boolean }) {
		this.url = url;
		this.withCredentials = init?.withCredentials ?? false;
		MockEventSource.last = this;
	}

	close() {
		this.closed = true;
	}

	emitOpen() {
		this.onopen?.();
	}
	emitError() {
		this.onerror?.();
	}
	emitMessage(payload: unknown) {
		this.onmessage?.({ data: JSON.stringify(payload) });
	}
}

describe('liveUpdates store', () => {
	beforeEach(() => {
		vi.stubGlobal('EventSource', MockEventSource as unknown as typeof EventSource);
		MockEventSource.last = null;
	});

	afterEach(() => {
		liveUpdates.disconnect();
		vi.unstubAllGlobals();
	});

	it('should open a credentialed EventSource to the events endpoint on connect', () => {
		liveUpdates.connect();

		expect(MockEventSource.last).not.toBeNull();
		// API_BASE already ends in /api/v1; the URL must be exactly `${API_BASE}/events`, not a
		// doubled `${API_BASE}/api/v1/events` (which 404s the SSE stream in every environment).
		expect(MockEventSource.last!.url).toBe(`${API_BASE}/events`);
		expect(MockEventSource.last!.url).not.toContain('/api/v1/api/v1');
		expect(MockEventSource.last!.withCredentials).toBe(true);
	});

	it('should not open a second connection when already connected', () => {
		liveUpdates.connect();
		const first = MockEventSource.last;
		liveUpdates.connect();

		expect(MockEventSource.last).toBe(first);
	});

	it('should reflect connected state from open and error events', () => {
		liveUpdates.connect();
		expect(liveUpdates.connected).toBe(false);

		MockEventSource.last!.emitOpen();
		expect(liveUpdates.connected).toBe(true);

		MockEventSource.last!.emitError();
		expect(liveUpdates.connected).toBe(false);
	});

	it('should dispatch parsed events to subscribers', () => {
		const received: LiveUpdateEvent[] = [];
		const unsubscribe = liveUpdates.subscribe((e) => received.push(e));
		liveUpdates.connect();

		MockEventSource.last!.emitMessage({ type: 'scrape-completed', productId: 'abc' });

		expect(received).toEqual([{ type: 'scrape-completed', productId: 'abc' }]);
		unsubscribe();
	});

	it('should stop dispatching after unsubscribe', () => {
		const received: LiveUpdateEvent[] = [];
		const unsubscribe = liveUpdates.subscribe((e) => received.push(e));
		liveUpdates.connect();
		unsubscribe();

		MockEventSource.last!.emitMessage({ type: 'notification' });

		expect(received).toHaveLength(0);
	});

	it('should ignore malformed frames without throwing', () => {
		const received: LiveUpdateEvent[] = [];
		liveUpdates.subscribe((e) => received.push(e));
		liveUpdates.connect();

		expect(() => MockEventSource.last!.onmessage?.({ data: 'not-json' })).not.toThrow();
		expect(received).toHaveLength(0);
	});

	it('should close the EventSource and clear connected on disconnect', () => {
		liveUpdates.connect();
		MockEventSource.last!.emitOpen();
		const source = MockEventSource.last!;

		liveUpdates.disconnect();

		expect(source.closed).toBe(true);
		expect(liveUpdates.connected).toBe(false);
	});
});
