import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { QuickAlert } from './quickAlert.svelte';
import { api, ApiError } from '$lib/api/client';

describe('QuickAlert', () => {
	beforeEach(() => {
		vi.useFakeTimers();
	});

	afterEach(() => {
		vi.useRealTimers();
		vi.restoreAllMocks();
	});

	it('should start closed with default state', () => {
		const qa = new QuickAlert();
		expect(qa.open).toBe(false);
		expect(qa.price).toBe(0);
		expect(qa.loading).toBe(false);
		expect(qa.success).toBe(false);
		expect(qa.error).toBe('');
		expect(qa.isNetworkError).toBe(false);
	});

	it('should toggle open with suggested 10% discount price', () => {
		const qa = new QuickAlert();
		qa.toggle(100);
		expect(qa.open).toBe(true);
		expect(qa.price).toBe(90);
	});

	it('should toggle open with 0 when currentPrice missing', () => {
		const qa = new QuickAlert();
		qa.toggle(null);
		expect(qa.open).toBe(true);
		expect(qa.price).toBe(0);
	});

	it('should toggle closed and reset transient state', () => {
		const qa = new QuickAlert();
		qa.toggle(100);
		qa.error = 'something';
		qa.loading = true;
		qa.toggle(100);
		expect(qa.open).toBe(false);
		expect(qa.error).toBe('');
		expect(qa.loading).toBe(false);
	});

	it('should call api.createAlert with productId, price, "below" on submit', async () => {
		const spy = vi.spyOn(api, 'createAlert').mockResolvedValue({} as never);
		const qa = new QuickAlert();
		qa.toggle(100);
		await qa.submit('product-1');
		expect(spy).toHaveBeenCalledWith('product-1', 90, 'below');
	});

	it('should invoke onCreated callback on success', async () => {
		vi.spyOn(api, 'createAlert').mockResolvedValue({} as never);
		const onCreated = vi.fn();
		const qa = new QuickAlert(onCreated);
		qa.toggle(100);
		await qa.submit('p1');
		expect(qa.success).toBe(true);
		expect(onCreated).toHaveBeenCalledTimes(1);
	});

	it('should auto-close after success timer fires', async () => {
		vi.spyOn(api, 'createAlert').mockResolvedValue({} as never);
		const qa = new QuickAlert();
		qa.toggle(100);
		await qa.submit('p1');
		expect(qa.open).toBe(true);
		expect(qa.success).toBe(true);
		vi.advanceTimersByTime(2000);
		expect(qa.open).toBe(false);
		expect(qa.success).toBe(false);
	});

	it('should surface field error from ApiError', async () => {
		const apiErr = new ApiError('Validation', 'invalid', { TargetPrice: ['Too low'] });
		vi.spyOn(api, 'createAlert').mockRejectedValue(apiErr);
		const qa = new QuickAlert();
		qa.toggle(100);
		await qa.submit('p1');
		expect(qa.error).toBe('Too low');
		expect(qa.isNetworkError).toBe(false);
		expect(qa.loading).toBe(false);
	});

	it('should surface ApiError message when no field errors', async () => {
		const apiErr = new ApiError('Conflict', 'Alert already exists');
		vi.spyOn(api, 'createAlert').mockRejectedValue(apiErr);
		const qa = new QuickAlert();
		qa.toggle(100);
		await qa.submit('p1');
		expect(qa.error).toBe('Alert already exists');
		expect(qa.isNetworkError).toBe(false);
	});

	it('should flag network error for non-ApiError exceptions', async () => {
		vi.spyOn(api, 'createAlert').mockRejectedValue(new Error('fetch failed'));
		const qa = new QuickAlert();
		qa.toggle(100);
		await qa.submit('p1');
		expect(qa.error).toBe('fetch failed');
		expect(qa.isNetworkError).toBe(true);
	});

	it('should cancel pending close timer on destroy', async () => {
		vi.spyOn(api, 'createAlert').mockResolvedValue({} as never);
		const qa = new QuickAlert();
		qa.toggle(100);
		await qa.submit('p1');
		qa.destroy();
		vi.advanceTimersByTime(5000);
		// open stays whatever it was when destroy was called; no late mutation
		expect(qa.open).toBe(true);
	});
});
