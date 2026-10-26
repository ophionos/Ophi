import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import ShippingRatesSection from './ShippingRatesSection.svelte';
import { api } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return { ...actual, api: { putShippingRate: vi.fn(), deleteShippingRate: vi.fn() } };
});

const rates = [
	{ host: 'amazon.es', amount: 3, currency: 'EUR', freeAbove: 50 },
	{ host: 'shop.example', amount: 4.5, currency: 'USD', freeAbove: null }
];

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.putShippingRate).mockImplementation(async (host, data) => ({
		host,
		amount: data.amount,
		currency: data.currency,
		freeAbove: data.freeAbove ?? null
	}));
	vi.mocked(api.deleteShippingRate).mockResolvedValue(undefined as never);
});

describe('ShippingRatesSection', () => {
	it('should list each rate with host, amount and free-above when set', () => {
		render(ShippingRatesSection, { props: { rates } });
		const rows = screen.getAllByTestId('shipping-rate-row');
		expect(rows).toHaveLength(2);
		expect(rows[0]).toHaveTextContent('amazon.es');
		expect(rows[0]).toHaveTextContent('€3.00');
		expect(rows[0]).toHaveTextContent('free above €50.00');
		expect(rows[1]).toHaveTextContent('$4.50');
		expect(rows[1]).not.toHaveTextContent('free above');
	});

	it('should show an empty message when there are no rates', () => {
		render(ShippingRatesSection, { props: { rates: [] } });
		expect(screen.getByText(/No shipping costs set/)).toBeInTheDocument();
	});

	it('should add a rate through the modal when the form is saved', async () => {
		render(ShippingRatesSection, { props: { rates: [] } });
		await fireEvent.click(screen.getByRole('button', { name: 'Add shipping cost' }));
		await fireEvent.input(screen.getByLabelText('Site host'), { target: { value: 'b.com' } });
		await fireEvent.input(screen.getByLabelText('Shipping cost'), { target: { value: '2' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

		await waitFor(() => expect(screen.getAllByTestId('shipping-rate-row')).toHaveLength(1));
		expect(screen.getByTestId('shipping-rate-row')).toHaveTextContent('b.com');
	});

	it('should open the modal with a read-only host when editing and update the row', async () => {
		render(ShippingRatesSection, { props: { rates } });
		await fireEvent.click(screen.getByRole('button', { name: 'Edit shipping cost for amazon.es' }));
		expect(screen.getByLabelText('Site host')).toHaveAttribute('readonly');
		await fireEvent.input(screen.getByLabelText('Shipping cost'), { target: { value: '9' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

		await waitFor(() =>
			expect(screen.getAllByTestId('shipping-rate-row')[0]).toHaveTextContent('€9.00')
		);
	});

	it('should ask for confirmation in the page before deleting', async () => {
		render(ShippingRatesSection, { props: { rates } });
		await fireEvent.click(
			screen.getByRole('button', { name: 'Delete shipping cost for amazon.es' })
		);

		expect(api.deleteShippingRate).not.toHaveBeenCalled();
		const dialog = screen.getByRole('dialog');
		await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

		await waitFor(() => expect(api.deleteShippingRate).toHaveBeenCalledWith('amazon.es'));
		await waitFor(() => expect(screen.getAllByTestId('shipping-rate-row')).toHaveLength(1));
	});

	it('should keep the rate when the confirmation is cancelled', async () => {
		render(ShippingRatesSection, { props: { rates } });
		await fireEvent.click(
			screen.getByRole('button', { name: 'Delete shipping cost for amazon.es' })
		);
		await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
		expect(api.deleteShippingRate).not.toHaveBeenCalled();
		expect(screen.getAllByTestId('shipping-rate-row')).toHaveLength(2);
	});
});
