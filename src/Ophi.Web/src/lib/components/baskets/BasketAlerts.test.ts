import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import BasketAlerts from './BasketAlerts.svelte';
import { api, ApiError, type BasketAlert, type BasketStore } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			createBasketAlert: vi.fn(),
			setBasketAlertActive: vi.fn(),
			deleteBasketAlert: vi.fn()
		}
	};
});

function alert(overrides: Partial<BasketAlert> = {}): BasketAlert {
	return {
		id: 'a1',
		basketId: 'b1',
		host: null,
		targetTotal: 50,
		currency: 'EUR',
		active: true,
		lastTriggered: null,
		triggerCount: 0,
		createdAt: '2026-10-01T00:00:00Z',
		watchedHost: 'shop-a.es',
		watchedTotal: 55,
		watchedCurrency: 'EUR',
		hasCurrencyMismatch: false,
		...overrides
	};
}

function store(host: string, complete = true): BasketStore {
	return {
		host,
		currency: 'EUR',
		items: [],
		subtotal: 55,
		shipping: 0,
		total: complete ? 55 : null,
		complete,
		missingCount: complete ? 0 : 1
	};
}

function props(overrides: Record<string, unknown> = {}) {
	return {
		basketId: 'b1',
		alerts: [] as BasketAlert[],
		stores: [store('shop-a.es'), store('shop-b.es'), store('partial.es', false)],
		...overrides
	};
}

describe('BasketAlerts', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	it('should explain when an alert is checked when the section renders', () => {
		render(BasketAlerts, props());

		expect(screen.getByText(/quantity or shipping-cost change/i)).toBeInTheDocument();
	});

	it('should show the target and the watched total when an alert exists', () => {
		render(BasketAlerts, props({ alerts: [alert()] }));

		const row = screen.getByTestId('basket-alert-row');
		expect(within(row).getByText(/Under €50\.00 at the best store/)).toBeInTheDocument();
		expect(within(row).getByText(/€55\.00 now at shop-a\.es/)).toBeInTheDocument();
	});

	it('should name the store when an alert watches one store', () => {
		render(BasketAlerts, props({ alerts: [alert({ host: 'shop-b.es', watchedHost: 'shop-b.es' })] }));

		expect(screen.getByText(/Under €50\.00 at shop-b\.es/)).toBeInTheDocument();
	});

	it('should warn when the watched total is in another currency', () => {
		render(BasketAlerts, props({ alerts: [alert({ hasCurrencyMismatch: true, watchedCurrency: 'USD' })] }));

		expect(screen.getByText(/another currency/i)).toBeInTheDocument();
	});

	it('should offer only complete stores when choosing a store', () => {
		render(BasketAlerts, props());

		const select = screen.getByLabelText('Store') as HTMLSelectElement;
		const values = [...select.options].map((o) => o.value);
		expect(values).toEqual(['', 'shop-a.es', 'shop-b.es']);
	});

	it('should create an alert and list it when the form is submitted', async () => {
		vi.mocked(api.createBasketAlert).mockResolvedValue(alert({ id: 'new', host: 'shop-b.es', targetTotal: 40 }));
		render(BasketAlerts, props());

		await fireEvent.input(screen.getByLabelText('Target total'), { target: { value: '40' } });
		await fireEvent.change(screen.getByLabelText('Store'), { target: { value: 'shop-b.es' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Add alert' }));

		await waitFor(() => expect(api.createBasketAlert).toHaveBeenCalledWith('b1', { targetTotal: 40, host: 'shop-b.es' }));
		expect(await screen.findByText(/Under €40\.00 at shop-b\.es/)).toBeInTheDocument();
	});

	it('should send a null host when the best store is chosen', async () => {
		vi.mocked(api.createBasketAlert).mockResolvedValue(alert());
		render(BasketAlerts, props());

		await fireEvent.input(screen.getByLabelText('Target total'), { target: { value: '50' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Add alert' }));

		await waitFor(() => expect(api.createBasketAlert).toHaveBeenCalledWith('b1', { targetTotal: 50, host: null }));
	});

	it('should show the server reason when the alert cannot be created', async () => {
		vi.mocked(api.createBasketAlert).mockRejectedValue(
			new ApiError('NoWatchedTotal', 'No store has every item of this basket in one currency yet, so there is no total to watch.')
		);
		render(BasketAlerts, props());

		await fireEvent.input(screen.getByLabelText('Target total'), { target: { value: '50' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Add alert' }));

		expect(await screen.findByText(/there is no total to watch/)).toBeInTheDocument();
	});

	it('should not submit when the target is not positive', async () => {
		render(BasketAlerts, props());

		await fireEvent.input(screen.getByLabelText('Target total'), { target: { value: '0' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Add alert' }));

		expect(api.createBasketAlert).not.toHaveBeenCalled();
		expect(await screen.findByText(/greater than 0/i)).toBeInTheDocument();
	});

	it('should pause an active alert when Pause is clicked', async () => {
		vi.mocked(api.setBasketAlertActive).mockResolvedValue(alert({ active: false }));
		render(BasketAlerts, props({ alerts: [alert()] }));

		await fireEvent.click(screen.getByRole('button', { name: 'Pause alert' }));

		await waitFor(() => expect(api.setBasketAlertActive).toHaveBeenCalledWith('b1', 'a1', false));
		expect(await screen.findByRole('button', { name: 'Resume alert' })).toBeInTheDocument();
		expect(screen.getByText('Paused')).toBeInTheDocument();
	});

	it('should delete an alert when the delete is confirmed', async () => {
		vi.mocked(api.deleteBasketAlert).mockResolvedValue(undefined);
		render(BasketAlerts, props({ alerts: [alert()] }));

		await fireEvent.click(screen.getByRole('button', { name: 'Delete alert' }));
		await fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));

		await waitFor(() => expect(api.deleteBasketAlert).toHaveBeenCalledWith('b1', 'a1'));
		await waitFor(() => expect(screen.queryByTestId('basket-alert-row')).not.toBeInTheDocument());
	});
});
