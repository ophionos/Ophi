import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fx } from './fx.svelte';
import { api } from '$lib/api/client';

vi.mock('$lib/api/client', () => ({
	api: { getSettings: vi.fn(), getFxRates: vi.fn() }
}));

const rates = { base: 'EUR', asOf: '2026-09-24T00:00:00Z', rates: { EUR: 1, USD: 1.1367 }, supported: [] };

beforeEach(() => {
	vi.clearAllMocks();
	fx.reset();
	vi.mocked(api.getFxRates).mockResolvedValue(rates);
});

describe('fx store', () => {
	it('should not fetch rates when the user has no display currency', async () => {
		vi.mocked(api.getSettings).mockResolvedValue({ displayCurrency: null } as never);
		await fx.load();
		expect(api.getFxRates).not.toHaveBeenCalled();
		expect(fx.displayCurrency).toBeNull();
	});

	it('should fetch rates once when a display currency is set, sharing concurrent loads', async () => {
		vi.mocked(api.getSettings).mockResolvedValue({ displayCurrency: 'USD' } as never);
		await Promise.all([fx.load(), fx.load()]);
		await fx.load();
		expect(api.getSettings).toHaveBeenCalledTimes(1);
		expect(api.getFxRates).toHaveBeenCalledTimes(1);
		expect(fx.displayCurrency).toBe('USD');
		expect(fx.rates.USD).toBe(1.1367);
	});

	it('should leave conversion off when loading fails', async () => {
		vi.mocked(api.getSettings).mockRejectedValue(new Error('offline'));
		await fx.load();
		expect(fx.displayCurrency).toBeNull();
	});

	it('should fetch rates when a currency is chosen later', async () => {
		await fx.setDisplayCurrency('GBP');
		expect(api.getFxRates).toHaveBeenCalledTimes(1);
		expect(fx.displayCurrency).toBe('GBP');
	});
});
