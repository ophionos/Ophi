import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { flushSync } from 'svelte';
import OfflineBanner from './OfflineBanner.svelte';
import { DATA_SOURCE_MESSAGE } from '$lib/offline/cache-policy';

describe('OfflineBanner', () => {
	let serviceWorker: EventTarget;

	beforeEach(() => {
		serviceWorker = new EventTarget();
		Object.defineProperty(navigator, 'serviceWorker', { value: serviceWorker, configurable: true });
		Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });
	});

	afterEach(() => {
		Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });
	});

	function postFromWorker(data: unknown) {
		serviceWorker.dispatchEvent(new MessageEvent('message', { data }));
		flushSync();
	}

	it('should be hidden when the browser is online', () => {
		render(OfflineBanner);

		expect(screen.queryByTestId('offline-banner')).not.toBeInTheDocument();
	});

	it('should show when the browser goes offline', () => {
		render(OfflineBanner);

		window.dispatchEvent(new Event('offline'));
		flushSync();

		expect(screen.getByTestId('offline-banner')).toHaveTextContent('You are offline');
	});

	it('should show when the page loads offline', () => {
		Object.defineProperty(navigator, 'onLine', { value: false, configurable: true });

		render(OfflineBanner);

		expect(screen.getByTestId('offline-banner')).toBeInTheDocument();
	});

	it('should show the oldest saved time when the worker serves saved data', () => {
		render(OfflineBanner);
		const older = new Date(2026, 9, 1, 9, 30).getTime();
		const newer = new Date(2026, 9, 3, 9, 30).getTime();

		postFromWorker({ type: DATA_SOURCE_MESSAGE, offline: true, cachedAt: newer });
		postFromWorker({ type: DATA_SOURCE_MESSAGE, offline: true, cachedAt: older });

		const banner = screen.getByTestId('offline-banner');
		expect(banner).toHaveTextContent('Prices from');
		expect(banner).toHaveTextContent(
			new Date(older).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
		);
	});

	it('should hide when the worker reaches the server again', () => {
		render(OfflineBanner);
		postFromWorker({ type: DATA_SOURCE_MESSAGE, offline: true, cachedAt: Date.now() });

		postFromWorker({ type: DATA_SOURCE_MESSAGE, offline: false });

		expect(screen.queryByTestId('offline-banner')).not.toBeInTheDocument();
	});

	it('should hide when the browser comes back online', () => {
		render(OfflineBanner);
		window.dispatchEvent(new Event('offline'));
		flushSync();

		window.dispatchEvent(new Event('online'));
		flushSync();

		expect(screen.queryByTestId('offline-banner')).not.toBeInTheDocument();
	});

	it('should ignore messages when they are not data-source messages', () => {
		render(OfflineBanner);

		postFromWorker({ type: 'something-else', offline: true });

		expect(screen.queryByTestId('offline-banner')).not.toBeInTheDocument();
	});
});
