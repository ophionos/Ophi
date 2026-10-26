import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import BasketShipping from './BasketShipping.svelte';
import type { ShippingRate } from '$lib/api/client';

const rate = { host: 'shop.es', amount: 4, currency: 'USD', freeAbove: null } as ShippingRate;

describe('BasketShipping', () => {
	it('should say other currency when the rate is in another currency than the store', () => {
		render(BasketShipping, { host: 'shop.es', shipping: null, currency: 'EUR', rates: [rate] });

		expect(screen.getByText('other currency')).toBeInTheDocument();
	});

	it('should show no shipping amount when the store mixes currencies', () => {
		render(BasketShipping, { host: 'shop.es', shipping: null, currency: null, rates: [rate] });

		expect(screen.queryByText('other currency')).not.toBeInTheDocument();
		expect(screen.getByTitle('Prices in different currencies')).toHaveTextContent('—');
	});
});
