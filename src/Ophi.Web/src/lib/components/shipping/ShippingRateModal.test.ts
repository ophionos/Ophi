import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ShippingRateModal from './ShippingRateModal.svelte';
import { api, ApiError } from '$lib/api/client';
import { fx } from '$lib/stores/fx.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return { ...actual, api: { putShippingRate: vi.fn() } };
});

const onClose = vi.fn();
const onSaved = vi.fn();

function open(props: Record<string, unknown> = {}) {
	return render(ShippingRateModal, { props: { isOpen: true, onClose, onSaved, ...props } });
}

beforeEach(() => {
	vi.clearAllMocks();
	fx.reset();
	vi.mocked(api.putShippingRate).mockImplementation(async (host, data) => ({
		host,
		amount: data.amount,
		currency: data.currency,
		freeAbove: data.freeAbove ?? null
	}));
});

describe('ShippingRateModal', () => {
	it('should default the currency to EUR when the user has no display currency', () => {
		open();
		expect(screen.getByLabelText('Currency')).toHaveValue('EUR');
	});

	it('should default the currency to the display currency when one is set', () => {
		fx.set({ displayCurrency: 'GBP', rates: {}, asOf: null });
		open();
		expect(screen.getByLabelText('Currency')).toHaveValue('GBP');
	});

	it('should save a new rate when the form is submitted', async () => {
		open();
		await fireEvent.input(screen.getByLabelText('Site host'), { target: { value: 'amazon.es' } });
		await fireEvent.input(screen.getByLabelText('Shipping cost'), { target: { value: '3.5' } });
		await fireEvent.input(screen.getByLabelText(/Free above/), { target: { value: '50' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

		await waitFor(() =>
			expect(api.putShippingRate).toHaveBeenCalledWith('amazon.es', {
				amount: 3.5,
				currency: 'EUR',
				freeAbove: 50
			})
		);
		await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ host: 'amazon.es' })));
	});

	it('should omit freeAbove when the field is empty', async () => {
		open({ prefill: { host: 'a.com', currency: 'USD' } });
		await fireEvent.input(screen.getByLabelText('Shipping cost'), { target: { value: '4' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
		await waitFor(() =>
			expect(api.putShippingRate).toHaveBeenCalledWith('a.com', {
				amount: 4,
				currency: 'USD',
				freeAbove: null
			})
		);
	});

	it('should prefill the host without www and lowercase, and the currency, from a URL host', () => {
		open({ prefill: { host: 'WWW.Amazon.ES', currency: 'EUR' } });
		expect(screen.getByLabelText('Site host')).toHaveValue('amazon.es');
	});

	it('should make the host read-only and prefill values when editing', () => {
		open({ rate: { host: 'amazon.es', amount: 3, currency: 'EUR', freeAbove: 50 } });
		expect(screen.getByLabelText('Site host')).toHaveAttribute('readonly');
		expect(screen.getByLabelText('Site host')).toHaveValue('amazon.es');
		expect(screen.getByLabelText('Shipping cost')).toHaveValue(3);
		expect(screen.getByLabelText(/Free above/)).toHaveValue(50);
	});

	it('should show server validation messages next to their fields when the server returns 400', async () => {
		vi.mocked(api.putShippingRate).mockRejectedValue(
			new ApiError('ValidationError', 'Validation failed', {
				Currency: ['Currency must be a 3-letter code (e.g. EUR)'],
				FreeAbove: ['Free-above must be greater than 0']
			})
		);
		open({ prefill: { host: 'a.com', currency: 'EU' } });
		await fireEvent.input(screen.getByLabelText('Shipping cost'), { target: { value: '4' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

		expect(await screen.findByText('Currency must be a 3-letter code (e.g. EUR)')).toBeInTheDocument();
		expect(screen.getByText('Free-above must be greater than 0')).toBeInTheDocument();
		expect(onSaved).not.toHaveBeenCalled();
	});

	it('should show a form error when the amount is empty without calling the server', async () => {
		open({ prefill: { host: 'a.com', currency: 'EUR' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
		expect(await screen.findByText('Enter a shipping cost (0 or more)')).toBeInTheDocument();
		expect(api.putShippingRate).not.toHaveBeenCalled();
	});
});
