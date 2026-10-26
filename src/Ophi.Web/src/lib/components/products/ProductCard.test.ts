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

	async function openMenu(name = 'Test Headphones') {
		await fireEvent.click(screen.getByRole('button', { name: `Actions for ${name}` }));
	}

	async function openQuickAlert() {
		await openMenu();
		await fireEvent.click(screen.getByRole('menuitem', { name: /Set price alert/ }));
	}

	beforeEach(() => {
		vi.clearAllMocks();
		vi.mocked(api.createAlert).mockResolvedValue({ id: 'alert-1' } as Alert);
	});

	describe('Active Product', () => {
		it('should display product name, store and price when loaded', () => {
			render(ProductCard, { props: { product: mockProduct } });

			expect(screen.getByText('Test Headphones')).toBeInTheDocument();
			expect(screen.getByTestId('product-store')).toHaveTextContent('example.com');
			expect(screen.getByTestId('product-price')).toHaveTextContent('$99.99');
			expect(screen.getByTestId('product-price')).toHaveClass('tabular-nums');
		});

		it('should show the 7-day change chip when the price moved', () => {
			render(ProductCard, { props: { product: { ...mockProduct, change7d: -6.2 } } });

			expect(screen.getByLabelText('Down 6.2%')).toBeInTheDocument();
		});

		it('should show New on its own line when there is no 7-day change', () => {
			render(ProductCard, { props: { product: { ...mockProduct, change7d: null } } });

			expect(screen.getByTestId('change-new')).toHaveTextContent('New');
		});

		it('should end the store line with the new cheapest store note when it is set', () => {
			render(ProductCard, { props: { product: { ...mockProduct, storeCount: 2, newCheapestStore: true } } });

			expect(screen.getByTestId('product-store')).toHaveTextContent('example.com · 1 more · new cheapest store');
		});

		it('should show the tracked days when there is no sparkline', () => {
			const product = { ...mockProduct, trackedSince: new Date(Date.now() - 3 * 86_400_000 - 3_600_000).toISOString() };
			render(ProductCard, { props: { product } });

			expect(screen.getByTestId('tracked-days')).toHaveTextContent('tracked 3 days');
		});

		it('should render the image when imageUrl is provided', () => {
			const { container } = render(ProductCard, { props: { product: mockProduct } });

			expect(container.querySelector('img')).toHaveAttribute('src', 'https://example.com/image.jpg');
		});

		it('should show a small initial and no image area when there is no image', () => {
			const { container } = render(ProductCard, { props: { product: { ...mockProduct, imageUrl: undefined } } });

			expect(container.querySelector('img')).not.toBeInTheDocument();
			expect(screen.getByTestId('product-initial')).toHaveTextContent('T');
		});

		it('should show when the product was last checked', () => {
			render(ProductCard, { props: { product: mockProduct } });
			expect(screen.getByText(/^Checked /)).toBeInTheDocument();
		});

		it('should show "Never checked" when lastChecked is not provided', () => {
			render(ProductCard, { props: { product: { ...mockProduct, lastChecked: undefined } } });
			expect(screen.getByText('Never checked')).toBeInTheDocument();
		});

		it('should link the name to the product page', () => {
			render(ProductCard, { props: { product: mockProduct } });
			expect(screen.getByRole('link', { name: 'Test Headphones' })).toHaveAttribute('href', '/products/product-123');
		});

		it('should give the sparkline the 90-day range as its tooltip', () => {
			const product = {
				...mockProduct,
				priceMin: 89,
				priceMax: 120,
				sparkline: [100, 105, 99.99].map((price, i) => ({ date: `2024-01-1${i}`, price }))
			};
			render(ProductCard, { props: { product } });

			expect(screen.getByTestId('price-sparkline')).toHaveAttribute('title', '90 days: $89.00–$120.00');
			expect(screen.queryByTestId('price-range-bar')).not.toBeInTheDocument();
		});

		it('should show the closest alert target when one exists', () => {
			const withAlert: Product = {
				...mockProduct,
				closestAlert: { alertId: 'a1', targetPrice: 90, condition: 'below', gap: 9.99, isNear: false }
			};
			render(ProductCard, { props: { product: withAlert } });
			expect(screen.getByTestId('product-alert')).toHaveTextContent('≤ $90.00');
			expect(screen.getByTestId('product-alert')).toHaveTextContent('$9.99 away');
		});
	});

	describe('Attention', () => {
		it('should show the attention icon when the product needs action', () => {
			const blocked: Product = {
				...mockProduct,
				attention: { kind: 'blocked', isAction: true, productUrlId: 'u1', url: mockProduct.url, consecutiveFailures: 3 }
			};
			render(ProductCard, { props: { product: blocked } });
			expect(screen.getByTestId('attention-icon')).toHaveAttribute('data-kind', 'blocked');
			expect(screen.getByTestId('attention-icon')).toHaveAttribute('aria-label', 'Blocked');
		});

		it('should not show a status for a healthy product', () => {
			render(ProductCard, { props: { product: mockProduct } });
			expect(screen.queryByTestId('attention-icon')).not.toBeInTheDocument();
			expect(screen.queryByText('Active')).not.toBeInTheDocument();
		});
	});

	describe('Pending Product', () => {
		const pendingProduct = { ...mockProduct, status: 'pending' as const, currentPrice: undefined, imageUrl: undefined };

		it('should render loading messages instead of the name and price', () => {
			render(ProductCard, { props: { product: pendingProduct } });

			// The name slot and the price slot wait with the same wording; so does the footer.
			expect(screen.getAllByText('Fetching price…')).toHaveLength(3);
			for (const old of ['Loading product data...', 'Fetching price...', 'Processing...']) {
				expect(screen.queryByText(old)).not.toBeInTheDocument();
			}
		});

		it('should not offer history or an alert for a pending product', async () => {
			render(ProductCard, { props: { product: pendingProduct, onViewHistory: vi.fn() } });
			await openMenu();

			expect(screen.queryByRole('menuitem', { name: /Price history/ })).not.toBeInTheDocument();
			expect(screen.queryByRole('menuitem', { name: /Set price alert/ })).not.toBeInTheDocument();
		});
	});

	describe('Price Display', () => {
		it('should show "No price" when currentPrice is not set', () => {
			render(ProductCard, { props: { product: { ...mockProduct, currentPrice: undefined } } });
			expect(screen.getByText('No price')).toBeInTheDocument();
		});
	});

	describe('Menu', () => {
		it('should put every action in one 44 px menu', async () => {
			render(ProductCard, {
				props: { product: mockProduct, onDelete: vi.fn(), onViewHistory: vi.fn(), onStatusChange: vi.fn() }
			});

			expect(screen.getByRole('button', { name: 'Actions for Test Headphones' })).toHaveClass('min-h-11', 'min-w-11');
			await openMenu();
			for (const name of [/Open in store/, /Price history/, /Pause tracking/, /Set price alert/, /Delete/]) {
				expect(screen.getByRole('menuitem', { name })).toBeInTheDocument();
			}
		});

		it('should call onDelete when Delete is chosen', async () => {
			const onDelete = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onDelete } });
			await openMenu();
			await fireEvent.click(screen.getByRole('menuitem', { name: /Delete/ }));

			expect(onDelete).toHaveBeenCalledWith('product-123');
		});

		it('should call onViewHistory when Price history is chosen', async () => {
			const onViewHistory = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onViewHistory } });
			await openMenu();
			await fireEvent.click(screen.getByRole('menuitem', { name: /Price history/ }));

			expect(onViewHistory).toHaveBeenCalledWith(mockProduct);
		});

		it('should not offer Delete when onDelete is not provided', async () => {
			render(ProductCard, { props: { product: mockProduct } });
			await openMenu();
			expect(screen.queryByRole('menuitem', { name: /Delete/ })).not.toBeInTheDocument();
		});
	});

	describe('Favourite Toggle', () => {
		it('should not render the star when onFavouriteToggle is not provided', () => {
			render(ProductCard, { props: { product: mockProduct } });
			expect(screen.queryByRole('button', { name: /favourites/ })).not.toBeInTheDocument();
		});

		it('should call onFavouriteToggle with true when adding to favourites', async () => {
			const onFavouriteToggle = vi.fn();
			render(ProductCard, { props: { product: mockProduct, onFavouriteToggle } });

			const star = screen.getByRole('button', { name: 'Add Test Headphones to favourites' });
			expect(star).toHaveClass('min-h-11', 'min-w-11');
			await fireEvent.click(star);
			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', true);
		});

		it('should call onFavouriteToggle with false when removing from favourites', async () => {
			const onFavouriteToggle = vi.fn();
			render(ProductCard, { props: { product: { ...mockProduct, isFavourite: true }, onFavouriteToggle } });

			await fireEvent.click(screen.getByRole('button', { name: 'Remove Test Headphones from favourites' }));
			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', false);
		});
	});

	describe('Selection', () => {
		it('should toggle selection when the checkbox is clicked', async () => {
			const onSelectToggle = vi.fn();
			render(ProductCard, { props: { product: mockProduct, selectable: true, onSelectToggle } });

			await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Test Headphones' }));
			expect(onSelectToggle).toHaveBeenCalledWith('product-123');
		});
	});

	describe('Quick Alert', () => {
		it('should show quick alert form when Set price alert is chosen', async () => {
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();

			expect(screen.getByText('Alert below')).toBeInTheDocument();
			expect(screen.getByLabelText('Create alert')).toBeInTheDocument();
			expect(screen.getByLabelText('Cancel')).toBeInTheDocument();
			expect(screen.getByRole('region', { name: /Quick price alert for Test Headphones/ })).toBeInTheDocument();
		});

		it('should pre-fill price at 90% of current price', async () => {
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();

			const input = screen.getByRole('spinbutton') as HTMLInputElement;
			// 99.99 * 0.9 = 89.991, floored to 2 decimals = 89.99
			expect(parseFloat(input.value)).toBe(89.99);
		});

		it('should call api.createAlert on submit', async () => {
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();
			await fireEvent.click(screen.getByLabelText('Create alert'));

			expect(api.createAlert).toHaveBeenCalledWith('product-123', 89.99, 'below');
		});

		it('should show success message after alert created', async () => {
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByText('Alert set ✓')).toBeInTheDocument();
				expect(screen.getByText('Alert set ✓')).toHaveClass('text-success');
			});
		});

		it('should show error message on failure', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(new Error('Network error'));
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByRole('alert')).toHaveTextContent('Network error');
			});
		});

		it('should reset loading state when closed mid-request', async () => {
			// Stall the API call so loading stays active
			vi.mocked(api.createAlert).mockReturnValueOnce(new Promise(() => {}));
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();
			await fireEvent.click(screen.getByLabelText('Create alert'));
			expect(screen.getByLabelText('Create alert')).toBeDisabled();

			await fireEvent.click(screen.getByLabelText('Cancel'));
			expect(screen.queryByText('Alert below')).not.toBeInTheDocument();

			// Reopen — loading state should be cleared
			await openQuickAlert();
			expect(screen.getByLabelText('Create alert')).not.toBeDisabled();
		});

		it('should show field-level error for ApiError with field details', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(
				new ApiError('VALIDATION_ERROR', 'Validation failed', { targetprice: ['Must be positive'] })
			);
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByText('Must be positive')).toBeInTheDocument();
			});
			expect(screen.queryByLabelText('Retry creating alert')).not.toBeInTheDocument();
		});

		it('should show retry button for network errors', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(new ApiError('UnknownError', 'An error occurred'));
			render(ProductCard, { props: { product: mockProduct } });
			await openQuickAlert();
			await fireEvent.click(screen.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(screen.getByLabelText('Retry creating alert')).toBeInTheDocument();
			});
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
