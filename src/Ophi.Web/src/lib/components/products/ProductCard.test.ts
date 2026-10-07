import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ProductCard from './ProductCard.svelte';
import { ApiError, type Product, type Alert } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual<typeof import('$lib/api/client')>('$lib/api/client');
	return {
		...actual,
		api: {
			createAlert: vi.fn().mockResolvedValue({ id: 'alert-1' })
		}
	};
});

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

import { api } from '$lib/api/client';

describe('ProductCard', () => {
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

	beforeEach(() => {
		vi.clearAllMocks();
		vi.mocked(api.createAlert).mockResolvedValue({ id: 'alert-1' } as Alert);
	});

	describe('Active Product', () => {
		it('should render product name', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.getByText('Test Headphones')).toBeInTheDocument();
		});

		it('should render current price', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.getByText('$99.99')).toBeInTheDocument();
		});

		it('should render the price with tabular figures', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.getByText('$99.99').className).toContain('tabular-nums');
		});

		it('should render price change percentage in badge', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.getByText('9.1%')).toBeInTheDocument();
			expect(screen.getByTestId('price-change-badge')).toBeInTheDocument();
		});

		it('should render product image when imageUrl is provided', () => {
			render(ProductCard, { props: { product: mockProduct } });

			const img = screen.getByRole('img');
			expect(img).toHaveAttribute('src', 'https://example.com/image.jpg');
		});

		it('should render an accessible no-image fallback when imageUrl is not provided', () => {
			const productWithoutImage = { ...mockProduct, imageUrl: undefined };
			render(ProductCard, { props: { product: productWithoutImage } });

			expect(screen.getByRole('img', { name: /no image available/i })).toBeInTheDocument();
		});

		it('should render last checked date', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.getByText(/Updated/)).toBeInTheDocument();
		});

		it('should render "Never checked" when lastChecked is not provided', () => {
			const productNeverChecked = { ...mockProduct, lastChecked: undefined };
			render(ProductCard, { props: { product: productNeverChecked } });

			expect(screen.getByText('Never checked')).toBeInTheDocument();
		});

		it('should render external link to product URL', () => {
			render(ProductCard, { props: { product: mockProduct } });

			const externalLink = screen.getByLabelText('Open product in new tab');
			expect(externalLink).toHaveAttribute('href', 'https://example.com/product');
			expect(externalLink).toHaveAttribute('target', '_blank');
		});
	});

	describe('Pending Product', () => {
		const pendingProduct: Product = {
			...mockProduct,
			status: 'pending',
			currentPrice: undefined,
			imageUrl: undefined
		};

		it('should render loading message instead of product name', () => {
			render(ProductCard, { props: { product: pendingProduct } });

			expect(screen.getByText('Loading product data...')).toBeInTheDocument();
			expect(screen.queryByText('Test Headphones')).not.toBeInTheDocument();
		});

		it('should render "Fetching price..." message', () => {
			render(ProductCard, { props: { product: pendingProduct } });

			expect(screen.getByText('Fetching price...')).toBeInTheDocument();
		});

		it('should render "Processing..." status', () => {
			render(ProductCard, { props: { product: pendingProduct } });

			expect(screen.getByText('Processing...')).toBeInTheDocument();
		});

		it('should not render View history button for pending products', () => {
			const onViewHistory = vi.fn();
			render(ProductCard, { props: { product: pendingProduct, onViewHistory } });

			expect(screen.queryByLabelText('View price history')).not.toBeInTheDocument();
		});

		it('should not render quick alert button for pending products', () => {
			render(ProductCard, { props: { product: pendingProduct } });

			expect(screen.queryByLabelText('Set quick price alert')).not.toBeInTheDocument();
		});
	});

	describe('Price Display', () => {
		it('should render "Price unavailable" when currentPrice is not set', () => {
			const productNoPrice = { ...mockProduct, status: 'active' as const, currentPrice: undefined };
			render(ProductCard, { props: { product: productNoPrice } });

			expect(screen.getByText('Price unavailable')).toBeInTheDocument();
		});

		it('should not render price change when priceChange is not set', () => {
			const productNoChange = { ...mockProduct, priceChange: undefined };
			render(ProductCard, { props: { product: productNoChange } });

			expect(screen.queryByTestId('price-change-badge')).not.toBeInTheDocument();
		});

		it('should not render price change when priceChange is zero', () => {
			const productNoChange = { ...mockProduct, priceChange: 0 };
			render(ProductCard, { props: { product: productNoChange } });

			expect(screen.queryByTestId('price-change-badge')).not.toBeInTheDocument();
		});
	});

	describe('Callbacks', () => {
		it('should call onDelete when delete button is clicked', async () => {
			const onDelete = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onDelete } });

			await fireEvent.click(screen.getByLabelText('Delete product'));

			expect(onDelete).toHaveBeenCalledWith('product-123');
		});

		it('should render View history button when onViewHistory is provided', () => {
			const onViewHistory = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onViewHistory } });

			expect(screen.getByLabelText('View price history')).toBeInTheDocument();
		});

		it('should call onViewHistory when View history button is clicked', async () => {
			const onViewHistory = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onViewHistory } });

			await fireEvent.click(screen.getByLabelText('View price history'));

			expect(onViewHistory).toHaveBeenCalledWith(mockProduct);
		});

		it('should not render delete button when onDelete is not provided', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.queryByLabelText('Delete product')).not.toBeInTheDocument();
		});
	});

	describe('Navigation Links', () => {
		it('should have links to product detail page', () => {
			render(ProductCard, { props: { product: mockProduct } });

			const productLinks = screen
				.getAllByRole('link')
				.filter((link) => link.getAttribute('href') === '/products/product-123');
			expect(productLinks.length).toBeGreaterThanOrEqual(2);
		});

		it('should link product image to product detail page', () => {
			render(ProductCard, { props: { product: mockProduct } });

			const imageLinks = screen.getAllByRole('link');
			const imageLink = imageLinks.find((link) => link.querySelector('img'));
			expect(imageLink).toHaveAttribute('href', '/products/product-123');
		});
	});

	describe('Favourite Toggle', () => {
		it('should not render star button when onFavouriteToggle is not provided', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.queryByLabelText('Add to favourites')).not.toBeInTheDocument();
			expect(screen.queryByLabelText('Remove from favourites')).not.toBeInTheDocument();
		});

		it('should render star button when onFavouriteToggle is provided', () => {
			const onFavouriteToggle = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onFavouriteToggle } });

			expect(screen.getByLabelText('Add to favourites')).toBeInTheDocument();
		});

		it('should show "Remove from favourites" label when product is favourite', () => {
			const onFavouriteToggle = vi.fn();
			const favouriteProduct = { ...mockProduct, isFavourite: true };
			render(ProductCard, { props: { product: favouriteProduct, onFavouriteToggle } });

			expect(screen.getByLabelText('Remove from favourites')).toBeInTheDocument();
		});

		it('should call onFavouriteToggle with true when adding to favourites', async () => {
			const onFavouriteToggle = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onFavouriteToggle } });

			await fireEvent.click(screen.getByLabelText('Add to favourites'));

			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', true);
		});

		it('should call onFavouriteToggle with false when removing from favourites', async () => {
			const onFavouriteToggle = vi.fn();
			const favouriteProduct = { ...mockProduct, isFavourite: true };
			render(ProductCard, { props: { product: favouriteProduct, onFavouriteToggle } });

			await fireEvent.click(screen.getByLabelText('Remove from favourites'));

			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', false);
		});
	});

	describe('Store Count Badge', () => {
		it('should show store count badge when storeCount > 1', () => {
			const multiStoreProduct = { ...mockProduct, storeCount: 3 };
			render(ProductCard, { props: { product: multiStoreProduct } });

			const badge = screen.getByTestId('store-count-badge');
			expect(badge).toBeInTheDocument();
			expect(badge.textContent?.trim()).toBe('3');
		});

		it('should not show store count badge when storeCount is 1', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.queryByTestId('store-count-badge')).not.toBeInTheDocument();
		});
	});

	describe('Alert Count Badge', () => {
		it('should show alert count badge when alertCount > 0', () => {
			const productWithAlerts = { ...mockProduct, alertCount: 3 };
			render(ProductCard, { props: { product: productWithAlerts } });

			expect(screen.getByText('3')).toBeInTheDocument();
		});

		it('should not show alert count badge when alertCount is 0', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.queryByTestId('alert-count-badge')).not.toBeInTheDocument();
		});
	});

	describe('Out of Stock', () => {
		it('should show out of stock badge when product is out of stock', () => {
			const oosProduct = { ...mockProduct, isOutOfStock: true };
			render(ProductCard, { props: { product: oosProduct } });

			expect(screen.getByTestId('out-of-stock-badge')).toBeInTheDocument();
			expect(screen.getByTestId('out-of-stock-badge').textContent).toContain('Out of Stock');
		});

		it('should not show out of stock badge when product is in stock', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.queryByTestId('out-of-stock-badge')).not.toBeInTheDocument();
		});

		it('should show "Out of Stock" text instead of price when OOS with no price', () => {
			const oosNoPrice = { ...mockProduct, isOutOfStock: true, currentPrice: undefined };
			render(ProductCard, { props: { product: oosNoPrice } });

			// Both badge and price area show "Out of Stock" - just verify "Price unavailable" is not shown
			expect(screen.getAllByText(/Out of Stock/).length).toBeGreaterThanOrEqual(2);
			expect(screen.queryByText('Price unavailable')).not.toBeInTheDocument();
		});

		it('should show price when OOS but price is available', () => {
			const oosWithPrice = { ...mockProduct, isOutOfStock: true, currentPrice: 49.99 };
			render(ProductCard, { props: { product: oosWithPrice } });

			expect(screen.getByTestId('out-of-stock-badge')).toBeInTheDocument();
			expect(screen.getByText('$49.99')).toBeInTheDocument();
		});
	});

	describe('Price Anomaly', () => {
		it('should show anomaly badge when product has price anomaly', () => {
			const anomalyProduct = { ...mockProduct, hasPriceAnomaly: true };
			render(ProductCard, { props: { product: anomalyProduct } });

			expect(screen.getByTestId('anomaly-badge')).toBeInTheDocument();
			expect(screen.getByTestId('anomaly-badge').textContent).toContain('Price Anomaly');
		});

		it('should not show anomaly badge when product has no anomaly', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.queryByTestId('anomaly-badge')).not.toBeInTheDocument();
		});

		it('should show both out of stock and anomaly badges when both apply', () => {
			const bothProduct = { ...mockProduct, isOutOfStock: true, hasPriceAnomaly: true };
			render(ProductCard, { props: { product: bothProduct } });

			expect(screen.getByTestId('out-of-stock-badge')).toBeInTheDocument();
			expect(screen.getByTestId('anomaly-badge')).toBeInTheDocument();
		});
	});

	describe('Quick Alert', () => {
		it('should show quick alert form when bell clicked', async () => {
			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));

			expect(screen.getByText('Alert below')).toBeInTheDocument();
			expect(screen.getByLabelText('Create alert')).toBeInTheDocument();
			expect(screen.getByLabelText('Cancel')).toBeInTheDocument();
		});

		it('should pre-fill price at 90% of current price', async () => {
			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));

			const input = screen.getByRole('spinbutton') as HTMLInputElement;
			// 99.99 * 0.9 = 89.991, floored to 2 decimals = 89.99
			expect(parseFloat(input.value)).toBe(89.99);
		});

		it('should hide bell when product is pending', () => {
			const pendingProduct = { ...mockProduct, status: 'pending' as const, currentPrice: undefined, imageUrl: undefined };
			render(ProductCard, { props: { product: pendingProduct } });

			expect(screen.queryByLabelText('Set quick price alert')).not.toBeInTheDocument();
		});

		it('should call api.createAlert on submit', async () => {
			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			await fireEvent.click(screen.getByLabelText('Create alert'));

			expect(api.createAlert).toHaveBeenCalledWith('product-123', 89.99, 'below');
		});

		it('should show success message after alert created', async () => {
			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByText('Alert set ✓')).toBeInTheDocument();
			});
		});

		it('should show error message on failure', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(new Error('Network error'));

			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByRole('alert')).toHaveTextContent('Network error');
			});
		});

		it('should reset loading state when closed mid-request', async () => {
			// Stall the API call so loading stays active
			vi.mocked(api.createAlert).mockReturnValueOnce(new Promise(() => {}));

			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			await fireEvent.click(screen.getByLabelText('Create alert'));

			// Submit button should be disabled while loading
			expect(screen.getByLabelText('Create alert')).toBeDisabled();

			// Close the form mid-request
			await fireEvent.click(screen.getByLabelText('Cancel'));
			expect(screen.queryByText('Alert below')).not.toBeInTheDocument();

			// Reopen — loading state should be cleared
			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			expect(screen.getByLabelText('Create alert')).not.toBeDisabled();
		});

		it('should close form when X clicked', async () => {
			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			expect(screen.getByText('Alert below')).toBeInTheDocument();

			await fireEvent.click(screen.getByLabelText('Cancel'));
			expect(screen.queryByText('Alert below')).not.toBeInTheDocument();
		});
	});

	describe('Quick Alert Error Differentiation', () => {
		it('should show field-level error for ApiError with field details', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(
				new ApiError('VALIDATION_ERROR', 'Validation failed', { targetprice: ['Must be positive'] })
			);

			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByText('Must be positive')).toBeInTheDocument();
			});
			expect(screen.queryByLabelText('Retry creating alert')).not.toBeInTheDocument();
		});

		it('should show retry button for network errors', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(
				new ApiError('UnknownError', 'An error occurred')
			);

			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByLabelText('Retry creating alert')).toBeInTheDocument();
			});
		});
	});

	describe('Accessibility', () => {
		it('should have aria-label on no-image placeholder', () => {
			const productWithoutImage = { ...mockProduct, imageUrl: undefined };
			render(ProductCard, { props: { product: productWithoutImage } });

			expect(screen.getByRole('img', { name: /no image available/i })).toBeInTheDocument();
		});

		it('should have aria-label on all action buttons', () => {
			const onDelete = vi.fn();
			const onViewHistory = vi.fn();
			const onStatusChange = vi.fn();
			render(ProductCard, {
				props: { product: mockProduct, onDelete, onViewHistory, onStatusChange }
			});

			expect(screen.getByLabelText('Open product in new tab')).toBeInTheDocument();
			expect(screen.getByLabelText('View price history')).toBeInTheDocument();
			expect(screen.getByLabelText('Pause tracking')).toBeInTheDocument();
			expect(screen.getByLabelText('Set quick price alert')).toBeInTheDocument();
			expect(screen.getByLabelText('Delete product')).toBeInTheDocument();
		});

		it('should wrap quick-alert in role="region" with aria-label', async () => {
			render(ProductCard, { props: { product: mockProduct } });

			await fireEvent.click(screen.getByLabelText('Set quick price alert'));

			const region = screen.getByRole('region', { name: /Quick price alert for Test Headphones/ });
			expect(region).toBeInTheDocument();
		});
	});

	describe('Null Safety', () => {
		it('should render without errors when product.tags is undefined', () => {
			const productNoTags = { ...mockProduct, tags: undefined as unknown as Product['tags'] };
			render(ProductCard, { props: { product: productNoTags } });

			expect(screen.getByText('Test Headphones')).toBeInTheDocument();
		});
	});
});
