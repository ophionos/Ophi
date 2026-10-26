import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import SevenDayChange from './SevenDayChange.svelte';
import type { Product } from '$lib/api/client';

const base: Product = {
	id: 'p1',
	name: 'Desk Lamp',
	url: 'https://shop.example/lamp',
	currency: 'USD',
	status: 'active',
	isFavourite: false,
	isOutOfStock: false,
	storeCount: 1,
	alertCount: 0,
	tags: [],
	customFields: []
};

describe('SevenDayChange', () => {
	it('should show New when there is less than 7 days of history', () => {
		render(SevenDayChange, { props: { product: { ...base, change7d: null } } });

		expect(screen.getByTestId('change-new')).toHaveTextContent('New');
		expect(screen.queryByTestId('price-change-chip')).not.toBeInTheDocument();
	});

	it('should show the change chip when the 7-day change is known', () => {
		render(SevenDayChange, { props: { product: { ...base, change7d: -6.2 } } });

		expect(screen.getByLabelText('Down 6.2%')).toBeInTheDocument();
		expect(screen.queryByTestId('change-new')).not.toBeInTheDocument();
	});

	it('should show a dash when the price did not change', () => {
		render(SevenDayChange, { props: { product: { ...base, change7d: 0 } } });

		expect(screen.getByTestId('price-change-none')).toBeInTheDocument();
	});
});
