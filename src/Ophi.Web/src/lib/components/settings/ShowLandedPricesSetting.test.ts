import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ShowLandedPricesSetting from './ShowLandedPricesSetting.svelte';
import { api } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: { getSettings: vi.fn(), updateSettings: vi.fn() }
	};
});

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.getSettings).mockResolvedValue({ showLandedPrices: false } as never);
	vi.mocked(api.updateSettings).mockResolvedValue({} as never);
});

describe('ShowLandedPricesSetting', () => {
	it('should render the switch off when the setting is off', async () => {
		render(ShowLandedPricesSetting);
		const toggle = await screen.findByRole('switch', { name: 'Show prices with shipping' });
		await waitFor(() => expect(toggle).toBeEnabled());
		expect(toggle).toHaveAttribute('aria-checked', 'false');
	});

	it('should render the switch on when the setting is on', async () => {
		vi.mocked(api.getSettings).mockResolvedValue({ showLandedPrices: true } as never);
		render(ShowLandedPricesSetting);
		const toggle = await screen.findByRole('switch', { name: 'Show prices with shipping' });
		await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'));
	});

	it('should save true through updateSettings when switched on', async () => {
		render(ShowLandedPricesSetting);
		const toggle = await screen.findByRole('switch', { name: 'Show prices with shipping' });
		await waitFor(() => expect(toggle).toBeEnabled());

		await fireEvent.click(toggle);

		expect(api.updateSettings).toHaveBeenCalledWith({ showLandedPrices: true });
		await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'));
	});

	it('should keep the old state when saving fails', async () => {
		vi.mocked(api.updateSettings).mockRejectedValue(new Error('boom'));
		render(ShowLandedPricesSetting);
		const toggle = await screen.findByRole('switch', { name: 'Show prices with shipping' });
		await waitFor(() => expect(toggle).toBeEnabled());

		await fireEvent.click(toggle);

		await waitFor(() => expect(api.updateSettings).toHaveBeenCalled());
		await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'false'));
	});
});
