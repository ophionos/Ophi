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
		url: 'https://example.com/product',
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

	it('should render product name', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		expect(screen.getByText('Test Headphones')).toBeInTheDocument();
	});

	it('should render current price with currency', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		expect(screen.getByText('$99.99')).toBeInTheDocument();
	});

	it('should render price change indicator', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		expect(screen.getByText('9.1%')).toBeInTheDocument();
	});

	it('should render status badge', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		expect(screen.getByText('Active')).toBeInTheDocument();
	});

	it('should render product image', () => {
		render(ProductTableMobileCard, { props: { product: mockProduct } });

		const img = screen.getByRole('img');
		expect(img).toHaveAttribute('src', 'https://example.com/image.jpg');
	});

	it('should render N/A placeholder when no image', () => {
		const noImage = { ...mockProduct, imageUrl: undefined };
		render(ProductTableMobileCard, { props: { product: noImage } });

		expect(screen.getByRole('img', { name: 'No image available' })).toBeInTheDocument();
	});

	it('should render action buttons with aria-labels', () => {
		const onDelete = vi.fn();
		const onViewHistory = vi.fn();
		const onStatusChange = vi.fn();
		render(ProductTableMobileCard, {
			props: { product: mockProduct, onDelete, onViewHistory, onStatusChange }
		});

		expect(screen.getByLabelText('Open product in new tab')).toBeInTheDocument();
		expect(screen.getByLabelText('View price history')).toBeInTheDocument();
		expect(screen.getByLabelText('Pause tracking')).toBeInTheDocument();
		expect(screen.getByLabelText('Delete product')).toBeInTheDocument();
	});

	it('should call onDelete when delete button is clicked', async () => {
		const onDelete = vi.fn();
		render(ProductTableMobileCard, { props: { product: mockProduct, onDelete } });

		await fireEvent.click(screen.getByLabelText('Delete product'));
		expect(onDelete).toHaveBeenCalledWith('product-123');
	});

	it('should call onViewHistory when history button is clicked', async () => {
		const onViewHistory = vi.fn();
		render(ProductTableMobileCard, { props: { product: mockProduct, onViewHistory } });

		await fireEvent.click(screen.getByLabelText('View price history'));
		expect(onViewHistory).toHaveBeenCalledWith(mockProduct);
	});

	it('should show loading state for pending products', () => {
		const pending = { ...mockProduct, status: 'pending' as const, currentPrice: undefined, imageUrl: undefined };
		render(ProductTableMobileCard, { props: { product: pending } });

		expect(screen.getByText('Loading product data...')).toBeInTheDocument();
		expect(screen.getByText('Fetching...')).toBeInTheDocument();
	});

	it('should render tags when present', () => {
		const withTags = {
			...mockProduct,
			tags: [{ id: 't1', name: 'Electronics', color: '#3B82F6' }]
		};
		render(ProductTableMobileCard, { props: { product: withTags } });

		expect(screen.getByText('Electronics')).toBeInTheDocument();
	});

	it('should have min-w-10 min-h-10 touch targets on action buttons', () => {
		const onDelete = vi.fn();
		render(ProductTableMobileCard, { props: { product: mockProduct, onDelete } });

		const deleteBtn = screen.getByLabelText('Delete product');
		expect(deleteBtn).toHaveClass('min-w-10');
		expect(deleteBtn).toHaveClass('min-h-10');
	});
});
