import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import DisplayCurrencySetting from './DisplayCurrencySetting.svelte';
import { api } from '$lib/api/client';
import { fx } from '$lib/stores/fx.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: { getSettings: vi.fn(), getFxRates: vi.fn(), updateSettings: vi.fn() }
	};
});

beforeEach(() => {
	vi.clearAllMocks();
	fx.reset();
	vi.mocked(api.getSettings).mockResolvedValue({ displayCurrency: null } as never);
	vi.mocked(api.getFxRates).mockResolvedValue({
		base: 'EUR',
		asOf: '2026-09-24T00:00:00Z',
		rates: { EUR: 1, USD: 1.1367 },
		supported: ['EUR', 'GBP', 'USD']
	});
	vi.mocked(api.updateSettings).mockResolvedValue({} as never);
});

describe('DisplayCurrencySetting', () => {
	it('should list the server-supported currencies plus an Off option', async () => {
		render(DisplayCurrencySetting);
		const select = await screen.findByLabelText('Display currency');
		await waitFor(() => expect(select.querySelectorAll('option')).toHaveLength(4));
		expect(select).toHaveValue('');
	});

	it('should save the choice and switch conversion on', async () => {
		render(DisplayCurrencySetting);
		const select = await screen.findByLabelText('Display currency');
		await waitFor(() => expect(select.querySelectorAll('option')).toHaveLength(4));

		await fireEvent.change(select, { target: { value: 'USD' } });

		expect(api.updateSettings).toHaveBeenCalledWith({ displayCurrency: 'USD' });
		await waitFor(() => expect(fx.displayCurrency).toBe('USD'));
	});

	it('should save an empty string to turn conversion off', async () => {
		vi.mocked(api.getSettings).mockResolvedValue({ displayCurrency: 'USD' } as never);
		render(DisplayCurrencySetting);
		const select = await screen.findByLabelText('Display currency');
		await waitFor(() => expect(select).toHaveValue('USD'));

		await fireEvent.change(select, { target: { value: '' } });

		expect(api.updateSettings).toHaveBeenCalledWith({ displayCurrency: '' });
		await waitFor(() => expect(fx.displayCurrency).toBeNull());
	});
});
