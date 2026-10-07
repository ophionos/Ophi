import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import type { ProductDetail } from '$lib/api/client';
import ProductHero from './ProductHero.svelte';
import PriceStats from './PriceStats.svelte';
import ComparisonGroupPanel from './ComparisonGroupPanel.svelte';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

const product: ProductDetail = {
	id: 'p1',
	name: 'MacBook Pro',
	url: 'https://apple.com/macbook',
	currentPrice: 1999.99,
	previousPrice: 2099.99,
	priceChange: -4.8,
	currency: 'USD',
	lastChecked: '2025-01-15T12:00:00Z',
	status: 'active',
	isFavourite: false,
	isOutOfStock: false,
	storeCount: 1,
	alertCount: 0,
	tags: [{ id: 't1', name: 'Laptops', color: '#3366ff' }],
	customFields: [],
	urls: [
		{
			id: 'u1',
			url: 'https://apple.com/macbook',
			currentPrice: 1999.99,
			currency: 'USD',
			lastCheckedAt: '2025-01-15T12:00:00Z',
			failureCount: 0,
			status: 'active',
			isOutOfStock: false
		}
	],
	hasCurrencyMismatch: false,
	statistics: { min: 1899.99, max: 2099.99, average: 1999.99, current: 1999.99 },
	alerts: []
};

describe('ProductHero', () => {
	it('should render the product name as the page heading', () => {
		render(ProductHero, { props: { product } });
		expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('MacBook Pro');
	});

	it('should show the No Image placeholder when the product has no image', () => {
		render(ProductHero, { props: { product } });
		expect(screen.getByText('No Image')).toBeInTheDocument();
	});

	it('should render the product tags', () => {
		render(ProductHero, { props: { product } });
		expect(screen.getByText('Laptops')).toBeInTheDocument();
	});

	it('should show the default check interval when none is set', () => {
		render(ProductHero, { props: { product } });
		expect(screen.getByText(/Checks every 1h \(default\)/)).toBeInTheDocument();
	});
});

describe('PriceStats', () => {
	it('should render the four stat cards when there is enough history', () => {
		render(PriceStats, { props: { product, bestPriceUrlId: null } });
		for (const label of ['Lowest', 'Highest', 'Average', 'Current']) {
			expect(screen.getByText(label)).toBeInTheDocument();
		}
	});

	it('should show the single-price display when every stat is the same value', () => {
		const flat = {
			...product,
			statistics: { min: 50, max: 50, average: 50, current: 50 },
			currentPrice: 50
		};
		render(PriceStats, { props: { product: flat, bestPriceUrlId: null } });
		expect(screen.getByTestId('single-price-display')).toHaveTextContent(
			'Not enough history for statistics'
		);
	});
});

describe('ComparisonGroupPanel', () => {
	const groups = [
		{ id: 'g1', name: 'Laptops', productCount: 3 },
		{ id: 'g2', name: 'Headphones', productCount: 2 }
	];

	it('should show the current group with a link to it', () => {
		render(ComparisonGroupPanel, {
			props: { groups, currentGroupId: 'g1', onAdd: vi.fn(), onRemove: vi.fn() }
		});
		expect(screen.getByText('3 products in group')).toBeInTheDocument();
		expect(screen.getByRole('link', { name: 'View Group' })).toHaveAttribute('href', '/comparisons/g1');
	});

	it('should call onRemove when Remove is clicked', async () => {
		const onRemove = vi.fn();
		render(ComparisonGroupPanel, {
			props: { groups, currentGroupId: 'g1', onAdd: vi.fn(), onRemove }
		});
		await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
		expect(onRemove).toHaveBeenCalledOnce();
	});

	it('should call onAdd with the selected group from a labelled select', async () => {
		const onAdd = vi.fn();
		render(ComparisonGroupPanel, { props: { groups, onAdd, onRemove: vi.fn() } });
		const select = screen.getByLabelText('Add to comparison group:');
		await fireEvent.change(select, { target: { value: 'g2' } });
		expect(onAdd).toHaveBeenCalledWith('g2');
	});

	it('should link to comparisons when no groups exist', () => {
		render(ComparisonGroupPanel, { props: { groups: [], onAdd: vi.fn(), onRemove: vi.fn() } });
		expect(screen.getByRole('link', { name: 'Create one' })).toHaveAttribute('href', '/comparisons');
	});
});
