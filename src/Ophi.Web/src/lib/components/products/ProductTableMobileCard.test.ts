import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ProductTableMobileCard from './ProductTableMobileCard.svelte';
import type { Product } from '$lib/api/client';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

describe('ProductTableMobileCard', () => {
	const mockProduct: Product = {
		id: 'product-123',
		name: 'Test Headphones',
		url: 'https://www.example.com/product',
		imageUrl: 'https://example.com/image.jpg',
		currentPrice: 99.99,
		previousPrice: 109.99,
		priceChange: -9.1,
		currency: 'USD',
		lastChecked: '2024-01-15T12:00:00Z',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: []
	};

	it('should render product name and price when loaded', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		expect(screen.getByText('Test Headphones')).toBeInTheDocument();
		expect(screen.getByText('$99.99')).toBeInTheDocument();
		expect(screen.getByTestId('change-new')).toBeInTheDocument();
	});

	it('should show the store as the subtitle when the product is healthy', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		expect(screen.getByTestId('mobile-product-subtitle')).toHaveTextContent('example.com');
		expect(screen.queryByText('Active')).not.toBeInTheDocument();
	});

	it('should show the attention label as the subtitle when the product needs action', () => {
		const blocked: Product = {
			...mockProduct,
			attention: { kind: 'blocked', isAction: true, productUrlId: 'url-1', url: mockProduct.url, consecutiveFailures: 3 }
		};
		render(ProductTableMobileCard, { props: { product: blocked } });

		const subtitle = screen.getByTestId('mobile-product-subtitle');
		expect(subtitle).toHaveTextContent('Blocked');
		expect(subtitle).not.toHaveTextContent('example.com');
	});

	it('should show an initial instead of a placeholder when there is no image', () => {
		render(ProductTableMobileCard, { props: { product: { ...mockProduct, imageUrl: undefined } } });

		expect(screen.getByTestId('product-initial')).toHaveTextContent('T');
		expect(screen.queryByText('N/A')).not.toBeInTheDocument();
	});

	it('should render a sparkline when there are enough points', () => {
		const { container } = render(ProductTableMobileCard, {
			props: { product: { ...mockProduct, sparkline: [100, 105, 99.99].map((price, i) => ({ date: `2024-01-1${i}`, price })) } }
		});

		expect(container.querySelector('[data-testid="mobile-sparkline"] svg')).toBeInTheDocument();
	});

	it('should give the sparkline the 90-day range as its tooltip', () => {
		const product = { ...mockProduct, priceMin: 90, priceMax: 120, sparkline: [100, 105, 99.99].map((price, i) => ({ date: `2024-01-1${i}`, price })) };
		render(ProductTableMobileCard, { props: { product } });

		expect(screen.getByTestId('mobile-sparkline')).toHaveAttribute('title', '90 days: $90.00–$120.00');
	});

	it('should show New under the price when there is no 7-day change', () => {
		render(ProductTableMobileCard, { props: { product: { ...mockProduct, change7d: null } } });

		expect(screen.getByTestId('change-new')).toHaveTextContent('New');
	});

	it('should show the 7-day change chip under the price when it is known', () => {
		render(ProductTableMobileCard, { props: { product: { ...mockProduct, change7d: -6.2 } } });

		expect(screen.getByLabelText('Down 6.2%')).toBeInTheDocument();
		expect(screen.queryByTestId('change-new')).not.toBeInTheDocument();
	});

	it('should end the subtitle with the new cheapest store note when it is set', () => {
		render(ProductTableMobileCard, { props: { product: { ...mockProduct, storeCount: 2, newCheapestStore: true } } });

		expect(screen.getByTestId('mobile-product-subtitle')).toHaveTextContent('example.com · 1 more · new cheapest store');
	});

	it('should put the actions in one menu when handlers are given', async () => {
		const onDelete = vi.fn();
		render(ProductTableMobileCard, { props: { product: mockProduct, onDelete, onViewHistory: vi.fn(), onStatusChange: vi.fn() } });

		const trigger = screen.getByRole('button', { name: 'Actions for Test Headphones' });
		expect(trigger).toHaveClass('min-h-11', 'min-w-11');
		await fireEvent.click(trigger);
		expect(screen.getByRole('menuitem', { name: /Price history/ })).toBeInTheDocument();
		expect(screen.getByRole('menuitem', { name: /Pause tracking/ })).toBeInTheDocument();
		await fireEvent.click(screen.getByRole('menuitem', { name: /Delete/ }));
		expect(onDelete).toHaveBeenCalledWith('product-123');
	});

	it('should toggle the favourite with a 44 px target when the star is clicked', async () => {
		const onFavouriteToggle = vi.fn();
		render(ProductTableMobileCard, { props: { product: mockProduct, onFavouriteToggle } });

		const star = screen.getByRole('button', { name: 'Add Test Headphones to favourites' });
		expect(star).toHaveClass('min-h-11', 'min-w-11');
		await fireEvent.click(star);
		expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', true);
	});

	it('should select the row when selectable and the checkbox is clicked', async () => {
		const onSelectToggle = vi.fn();
		render(ProductTableMobileCard, { props: { product: mockProduct, selectable: true, onSelectToggle } });

		await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Test Headphones' }));
		expect(onSelectToggle).toHaveBeenCalledWith('product-123');
	});

	it('should show loading state for pending products', () => {
		const pending = { ...mockProduct, status: 'pending' as const, currentPrice: undefined, imageUrl: undefined };
		render(ProductTableMobileCard, { props: { product: pending } });

		expect(screen.getByText('Fetching price…')).toBeInTheDocument();
		expect(screen.queryByText('Loading product data...')).not.toBeInTheDocument();
		expect(screen.queryByText('Fetching...')).not.toBeInTheDocument();
	});
});
