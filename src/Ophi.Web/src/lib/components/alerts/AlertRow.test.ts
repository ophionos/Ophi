import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AlertRow from './AlertRow.svelte';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => `/base${path}`
}));

const baseAlert = {
	id: 'a1',
	targetPrice: 80,
	condition: 'below' as const,
	active: true,
	currency: 'USD',
	hasCurrencyMismatch: false
};

function renderRow(overrides: Record<string, unknown> = {}) {
	const props = {
		alert: baseAlert,
		productCurrency: 'USD',
		onDelete: vi.fn().mockResolvedValue(undefined),
		onRedenominate: vi.fn().mockResolvedValue(undefined),
		...overrides
	};
	render(AlertRow, { props });
	return props;
}

describe('AlertRow', () => {
	it('should show the condition and the target formatted in the alert currency', () => {
		renderRow();
		expect(screen.getByText(/Below:/)).toHaveTextContent('$80.00');
	});

	it('should show a percentage target for percentDrop alerts', () => {
		renderRow({ alert: { ...baseAlert, condition: 'percentDrop', targetPrice: 15 } });
		expect(screen.getByText(/Drop %:/)).toHaveTextContent('15%');
	});

	it('should show the last-triggered date when the alert has fired', () => {
		renderRow({ alert: { ...baseAlert, lastTriggered: '2026-09-01T10:00:00Z' } });
		expect(screen.getByText(/Last triggered/)).toBeInTheDocument();
	});

	it('should not link to the product when no product is given', () => {
		renderRow();
		expect(screen.queryByRole('link')).not.toBeInTheDocument();
	});

	it('should link to the product when a product is given', () => {
		renderRow({ product: { id: 'p1', name: 'Sony WH-1000XM5' } });
		const link = screen.getByRole('link', { name: 'Sony WH-1000XM5' });
		expect(link).toHaveAttribute('href', '/base/products/p1');
	});

	it('should call onDelete and disable the button while the delete is in flight', async () => {
		let finish!: () => void;
		const onDelete = vi.fn(() => new Promise<void>((r) => (finish = r)));
		renderRow({ onDelete });

		const button = screen.getByLabelText('Delete alert');
		await fireEvent.click(button);
		expect(onDelete).toHaveBeenCalledOnce();
		expect(button).toBeDisabled();

		finish();
		await waitFor(() => expect(button).not.toBeDisabled());
	});

	describe('pause toggle', () => {
		it('should not render a pause control when onToggleActive is not given', () => {
			renderRow();
			expect(screen.queryByLabelText('Pause alert')).not.toBeInTheDocument();
		});

		it('should offer Pause for an active alert and call onToggleActive', async () => {
			const onToggleActive = vi.fn().mockResolvedValue(undefined);
			renderRow({ onToggleActive });
			await fireEvent.click(screen.getByLabelText('Pause alert'));
			expect(onToggleActive).toHaveBeenCalledOnce();
		});

		it('should offer Resume and say Paused for a paused alert', () => {
			renderRow({ alert: { ...baseAlert, active: false }, onToggleActive: vi.fn() });
			expect(screen.getByLabelText('Resume alert')).toBeInTheDocument();
			expect(screen.getByText(/Paused/)).toBeInTheDocument();
		});
	});

	describe('selection', () => {
		it('should render a labelled checkbox when onSelectChange is given', async () => {
			const onSelectChange = vi.fn();
			renderRow({ product: { id: 'p1', name: 'Widget' }, selected: false, onSelectChange });
			const box = screen.getByRole('checkbox', { name: 'Select alert for Widget' });
			await fireEvent.click(box);
			expect(onSelectChange).toHaveBeenCalledWith(true);
		});
	});

	describe('dormant alert', () => {
		const dormant = { ...baseAlert, currency: 'USD', hasCurrencyMismatch: true };

		it('should explain the dormancy with both currencies', () => {
			renderRow({ alert: dormant, productCurrency: 'EUR' });
			expect(screen.getByTestId('alert-currency-mismatch')).toHaveTextContent(
				/target is in USD, product is now priced in\s+EUR/
			);
		});

		it('should open the redenominate form and close it after a successful submit', async () => {
			const onRedenominate = vi.fn().mockResolvedValue(undefined);
			renderRow({ alert: dormant, productCurrency: 'EUR', productCurrentPrice: 100, onRedenominate });

			await fireEvent.click(screen.getByTestId('redenominate-alert'));
			const form = screen.getByTestId('redenominate-form');
			await fireEvent.submit(form);

			expect(onRedenominate).toHaveBeenCalledWith(90);
			await waitFor(() => expect(screen.queryByTestId('redenominate-form')).not.toBeInTheDocument());
		});

		it('should keep the form open when the redenominate call fails', async () => {
			const onRedenominate = vi.fn().mockRejectedValue(new Error('Currency changed'));
			renderRow({ alert: dormant, productCurrency: 'EUR', productCurrentPrice: 100, onRedenominate });

			await fireEvent.click(screen.getByTestId('redenominate-alert'));
			await fireEvent.submit(screen.getByTestId('redenominate-form'));

			await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Currency changed'));
			expect(screen.getByTestId('redenominate-form')).toBeInTheDocument();
		});
	});
});
