import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import AlertList from './AlertList.svelte';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

const alerts = [
	{ id: 'a1', targetPrice: 80, condition: 'below' as const, active: true, currency: 'USD', hasCurrencyMismatch: false },
	{ id: 'a2', targetPrice: 10, condition: 'percentDrop' as const, active: true, currency: 'USD', hasCurrencyMismatch: false }
];

describe('AlertList', () => {
	it('should show the empty tip when there are no alerts', () => {
		render(AlertList, {
			props: { alerts: [], productCurrency: 'USD', onDelete: vi.fn(), onRedenominate: vi.fn() }
		});
		expect(screen.getByTestId('no-alerts-tip')).toHaveTextContent('Create Alert');
	});

	it('should render one row per alert', () => {
		render(AlertList, {
			props: { alerts, productCurrency: 'USD', onDelete: vi.fn(), onRedenominate: vi.fn() }
		});
		expect(screen.getAllByTestId('alert-row')).toHaveLength(2);
	});

	it('should pass the alert id to onDelete', async () => {
		const onDelete = vi.fn().mockResolvedValue(undefined);
		render(AlertList, {
			props: { alerts, productCurrency: 'USD', onDelete, onRedenominate: vi.fn() }
		});
		await fireEvent.click(screen.getAllByLabelText('Delete alert')[1]);
		expect(onDelete).toHaveBeenCalledWith('a2');
	});
});
