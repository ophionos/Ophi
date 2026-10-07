import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import CurrencyMismatchBanner from './CurrencyMismatchBanner.svelte';

describe('CurrencyMismatchBanner', () => {
	it('should render warning with listed currencies when multiple provided', () => {
		render(CurrencyMismatchBanner, { props: { currencies: ['USD', 'GBP'] } });
		expect(screen.getByTestId('currency-mismatch-warning')).toBeInTheDocument();
		expect(screen.getByText(/USD, GBP/)).toBeInTheDocument();
		expect(
			screen.getByText(/Direct comparison may not be accurate/)
		).toBeInTheDocument();
	});

	it('should not render when single currency', () => {
		render(CurrencyMismatchBanner, { props: { currencies: ['USD'] } });
		expect(screen.queryByTestId('currency-mismatch-warning')).not.toBeInTheDocument();
	});

	it('should not render when empty currencies', () => {
		render(CurrencyMismatchBanner, { props: { currencies: [] } });
		expect(screen.queryByTestId('currency-mismatch-warning')).not.toBeInTheDocument();
	});

	it('should have data-testid="currency-mismatch-warning"', () => {
		render(CurrencyMismatchBanner, { props: { currencies: ['USD', 'EUR', 'GBP'] } });
		const banner = screen.getByTestId('currency-mismatch-warning');
		expect(banner).toBeInTheDocument();
		expect(screen.getByText(/USD, EUR, GBP/)).toBeInTheDocument();
	});
});
