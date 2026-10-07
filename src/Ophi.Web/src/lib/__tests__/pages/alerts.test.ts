import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import AlertsPage from '../../../routes/alerts/+page.svelte';
import { load } from '../../../routes/alerts/+page';
import { page } from '$app/state';
import { replaceState } from '$app/navigation';
import { api, type Alert } from '$lib/api/client';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	const mockApi = {
		getAlerts: vi.fn(),
		setAlertActive: vi.fn(),
		deleteAlert: vi.fn(),
		redenominateAlert: vi.fn(),
		withFetch: vi.fn()
	};
	mockApi.withFetch.mockReturnValue(mockApi);
	return { ...actual, api: mockApi };
});

const now = Date.now();
const daysAgo = (d: number) => new Date(now - d * 86_400_000).toISOString();

function makeAlert(overrides: Partial<Alert>): Alert {
	return {
		id: 'a',
		productId: 'p',
		productName: 'Product',
		currentPrice: 100,
		targetPrice: 80,
		condition: 'below',
		active: true,
		currency: 'USD',
		productCurrency: 'USD',
		hasCurrencyMismatch: false,
		...overrides
	};
}

const alerts: Alert[] = [
	makeAlert({ id: 'a1', productId: 'p1', productName: 'Zebra Lamp', lastTriggered: daysAgo(2) }),
	makeAlert({ id: 'a2', productId: 'p2', productName: 'Alpha Mouse', active: false, lastTriggered: daysAgo(30) }),
	makeAlert({ id: 'a3', productId: 'p3', productName: 'Mid Desk', currency: 'USD', productCurrency: 'EUR', hasCurrencyMismatch: true })
];

function renderPage(url = 'http://localhost/alerts', items: Alert[] = alerts) {
	page.url = new URL(url) as unknown as typeof page.url;
	return render(AlertsPage, {
		props: { data: { user: { id: '1', email: 'a@b.com', name: 'A' }, alerts: items } as never }
	});
}

function rowNames() {
	return screen.getAllByTestId('alert-row').map((r) => within(r).getAllByRole('link')[0].textContent?.trim());
}

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.withFetch).mockReturnValue(api as never);
	vi.mocked(api.getAlerts).mockResolvedValue({ items: alerts });
});

describe('alerts loader', () => {
	it('should load alerts for a signed-in user', async () => {
		const result = await load({
			fetch: vi.fn(),
			parent: async () => ({ user: { id: '1' } })
		} as never);
		expect(result).toEqual({ alerts });
	});

	it('should return no alerts without a user', async () => {
		const result = await load({ fetch: vi.fn(), parent: async () => ({ user: null }) } as never);
		expect(result).toEqual({ alerts: [] });
		expect(api.getAlerts).not.toHaveBeenCalled();
	});
});

describe('Alerts page', () => {
	it('should render one row per alert, each linking to its product', () => {
		renderPage();
		expect(screen.getAllByTestId('alert-row')).toHaveLength(3);
		expect(screen.getByRole('link', { name: 'Zebra Lamp' })).toHaveAttribute('href', '/products/p1');
	});

	it('should sort by most recently fired by default, never-fired last', () => {
		renderPage();
		expect(rowNames()).toEqual(['Zebra Lamp', 'Alpha Mouse', 'Mid Desk']);
	});

	it('should sort by product name when ?sort=product', () => {
		renderPage('http://localhost/alerts?sort=product');
		expect(rowNames()).toEqual(['Alpha Mouse', 'Mid Desk', 'Zebra Lamp']);
	});

	it.each([
		['active', ['Zebra Lamp']],
		['paused', ['Alpha Mouse']],
		['dormant', ['Mid Desk']],
		['recent', ['Zebra Lamp']]
	])('should filter to %s alerts from the URL', (status, expected) => {
		renderPage(`http://localhost/alerts?status=${status}`);
		expect(rowNames()).toEqual(expected);
	});

	it('should mirror a filter chip into the URL via shallow replaceState', async () => {
		renderPage();
		await fireEvent.click(screen.getByRole('button', { name: /^Paused/ }));
		expect(rowNames()).toEqual(['Alpha Mouse']);
		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.toString()).toContain('status=paused');
	});

	it('should show a filter-specific message when nothing matches', () => {
		renderPage('http://localhost/alerts?status=paused', [alerts[0]]);
		expect(screen.getByText('No alerts match this filter')).toBeInTheDocument();
	});

	it('should show the empty state when the user has no alerts', () => {
		renderPage('http://localhost/alerts', []);
		expect(screen.getByText('No alerts yet')).toBeInTheDocument();
	});

	it('should pause an alert via the row toggle and update it in place', async () => {
		vi.mocked(api.setAlertActive).mockResolvedValue({ ...alerts[0], active: false });
		renderPage();
		const row = screen.getAllByTestId('alert-row')[0];
		await fireEvent.click(within(row).getByLabelText('Pause alert'));

		expect(api.setAlertActive).toHaveBeenCalledWith('a1', false);
		await waitFor(() => expect(within(row).getByLabelText('Resume alert')).toBeInTheDocument());
	});

	it('should toast the server message when resuming hits the alert cap', async () => {
		const spy = vi.spyOn(toast, 'error');
		vi.mocked(api.setAlertActive).mockRejectedValue(new Error('Maximum number of active alerts (100) reached.'));
		renderPage('http://localhost/alerts?status=paused');
		await fireEvent.click(screen.getByLabelText('Resume alert'));
		await waitFor(() => expect(spy).toHaveBeenCalledWith('Maximum number of active alerts (100) reached.'));
	});

	it('should bulk-pause the selected alerts and report partial failures', async () => {
		const spy = vi.spyOn(toast, 'error');
		vi.mocked(api.setAlertActive)
			.mockResolvedValueOnce({ ...alerts[0], active: false })
			.mockRejectedValueOnce(new Error('boom'));
		renderPage();
		await fireEvent.click(screen.getByRole('checkbox', { name: 'Select alert for Zebra Lamp' }));
		await fireEvent.click(screen.getByRole('checkbox', { name: 'Select alert for Mid Desk' }));
		await fireEvent.click(screen.getByRole('button', { name: 'Pause selected' }));

		await waitFor(() => expect(spy).toHaveBeenCalledWith('1 of 2 alerts failed to update'));
		expect(api.setAlertActive).toHaveBeenCalledWith('a1', false);
		expect(api.setAlertActive).toHaveBeenCalledWith('a3', false);
	});

	it('should bulk-delete the selected alerts after confirmation', async () => {
		vi.mocked(api.deleteAlert).mockResolvedValue(undefined as never);
		renderPage();
		await fireEvent.click(screen.getByRole('checkbox', { name: 'Select alert for Alpha Mouse' }));
		await fireEvent.click(screen.getByRole('button', { name: 'Delete selected' }));
		await fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

		await waitFor(() => expect(screen.getAllByTestId('alert-row')).toHaveLength(2));
		expect(api.deleteAlert).toHaveBeenCalledWith('a2');
	});
});
