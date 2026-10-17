import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { liveUpdates, type LiveUpdateEvent } from './liveUpdates.svelte';
import { API_BASE } from '$lib/api/client';

// Minimal EventSource stand-in: jsdom doesn't implement EventSource, and we want to drive
// open/message/error from the test rather than open a real network connection.
class MockEventSource {
	static last: MockEventSource | null = null;
	static count = 0;
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
		MockEventSource.count++;
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

	describe('reconnect backoff', () => {
		// Math.random() = 0 puts the jittered delay at its floor: half of the backoff step.
		// Steps are 2 s, 4 s, 8 s, ... capped at 60 s, so the floors are 1 s, 2 s, 4 s, ... 30 s.
		let hidden = false;

		beforeEach(() => {
			vi.useFakeTimers();
			vi.spyOn(Math, 'random').mockReturnValue(0);
			hidden = false;
			vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
			MockEventSource.count = 0;
		});

		afterEach(() => {
			liveUpdates.disconnect();
			vi.useRealTimers();
			vi.restoreAllMocks();
		});

		it('should close the failed EventSource instead of leaving the browser to retry', () => {
			liveUpdates.connect();
			const first = MockEventSource.last!;

			first.emitError();

			expect(first.closed).toBe(true);
			expect(MockEventSource.count).toBe(1);
		});

		it('should reconnect after the backoff delay', () => {
			liveUpdates.connect();
			MockEventSource.last!.emitError();

			vi.advanceTimersByTime(999);
			expect(MockEventSource.count).toBe(1);

			vi.advanceTimersByTime(1);
			expect(MockEventSource.count).toBe(2);
		});

		it('should double the delay on each consecutive failure up to the cap', () => {
			liveUpdates.connect();
			const floors = [1000, 2000, 4000, 8000, 16000, 30000, 30000];

			for (const [i, floor] of floors.entries()) {
				MockEventSource.last!.emitError();
				vi.advanceTimersByTime(floor - 1);
				expect(MockEventSource.count).toBe(i + 1);
				vi.advanceTimersByTime(1);
				expect(MockEventSource.count).toBe(i + 2);
			}
		});

		it('should reset the delay after a successful open', () => {
			liveUpdates.connect();
			MockEventSource.last!.emitError();
			vi.advanceTimersByTime(1000);
			MockEventSource.last!.emitError();
			vi.advanceTimersByTime(2000);

			MockEventSource.last!.emitOpen();
			MockEventSource.last!.emitError();
			vi.advanceTimersByTime(1000);

			expect(MockEventSource.count).toBe(4);
		});

		it('should not reconnect after disconnect', () => {
			liveUpdates.connect();
			MockEventSource.last!.emitError();

			liveUpdates.disconnect();
			vi.advanceTimersByTime(60_000);

			expect(MockEventSource.count).toBe(1);
		});

		it('should wait while the tab is hidden and reconnect when it becomes visible', () => {
			liveUpdates.connect();
			MockEventSource.last!.emitError();
			hidden = true;

			vi.advanceTimersByTime(60_000);
			expect(MockEventSource.count).toBe(1);

			hidden = false;
			document.dispatchEvent(new Event('visibilitychange'));
			expect(MockEventSource.count).toBe(2);
		});

		it('should not open a connection on visibility change while connected', () => {
			liveUpdates.connect();
			MockEventSource.last!.emitOpen();

			document.dispatchEvent(new Event('visibilitychange'));

			expect(MockEventSource.count).toBe(1);
		});
	});
});
