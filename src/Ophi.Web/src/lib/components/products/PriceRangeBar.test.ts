import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import PriceRangeBar from './PriceRangeBar.svelte';

describe('PriceRangeBar', () => {
	it('should place the marker by the current price and label the range', () => {
		render(PriceRangeBar, { props: { min: 50, max: 150, current: 75, currency: 'USD' } });

		const bar = screen.getByRole('img', { name: '90-day range $50.00 to $150.00, now $75.00' });
		expect(bar).toHaveTextContent('$50.00');
		expect(bar).toHaveTextContent('$150.00');
		expect(screen.getByTestId('range-marker').style.left).toBe('25%');
	});

	it('should keep the marker inside the track for a price outside the range', () => {
		render(PriceRangeBar, { props: { min: 50, max: 150, current: 10, currency: 'USD' } });
		expect(screen.getByTestId('range-marker').style.left).toBe('0%');
	});

	it('should not render when the range is a single price', () => {
		render(PriceRangeBar, { props: { min: 50, max: 50, current: 50, currency: 'USD' } });
		expect(screen.queryByTestId('price-range-bar')).not.toBeInTheDocument();
	});
});
