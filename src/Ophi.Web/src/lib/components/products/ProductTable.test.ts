import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import ProductTable from './ProductTable.svelte';
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
import { density } from '$lib/stores/density.svelte';

/**
 * Helper: scope queries to the desktop table (hidden md:block).
 * JSDOM renders both mobile cards and desktop table since media queries don't apply.
 */
function getDesktopTable() {
	return within(screen.getByTestId('product-table'));
}

/** Opens the row's action menu and picks an item; the menu items are found by role. */
async function chooseAction(productName: string, item: string) {
	await fireEvent.click(getDesktopTable().getByRole('button', { name: `Actions for ${productName}` }));
	await fireEvent.click(screen.getByRole('menuitem', { name: item }));
}

describe('ProductTable', () => {
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

	const mockProduct2: Product = {
		id: 'product-456',
		name: 'Test Keyboard',
		url: 'https://example.com/keyboard',
		currentPrice: 49.99,
		currency: 'USD',
		lastChecked: '2024-01-14T12:00:00Z',
		status: 'active',
		isFavourite: true,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: []
	};

	beforeEach(() => {
		vi.clearAllMocks();
		vi.mocked(api.createAlert).mockResolvedValue({ id: 'alert-1' } as Alert);
		// Density is a shared singleton — reset so prior tests don't leak compact mode.
		density.set('comfortable');
	});

	describe('Rendering', () => {
		it('should render the watchlist column headers', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const headers = Array.from(screen.getByTestId('product-table').querySelectorAll('th')).map((th) => th.textContent?.trim());

			expect(headers).toEqual(['Product', 'Status', 'Price', 'Change', '30 days', '90-day range', 'Alert', 'Checked', 'Actions']);
		});

		it('should render product rows with name', () => {
			render(ProductTable, { props: { products: [mockProduct, mockProduct2] } });
			const table = getDesktopTable();

			expect(table.getByText('Test Headphones')).toBeInTheDocument();
			expect(table.getByText('Test Keyboard')).toBeInTheDocument();
		});

		it('should render the price with tabular figures and the currency under it', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const price = getDesktopTable().getByTestId('product-price');

			expect(price).toHaveTextContent('$99.99');
			expect(price.className).toContain('tabular-nums');
			expect(price.nextElementSibling).toHaveTextContent('USD');
		});

		it('should render the change as a chip', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(getDesktopTable().getByLabelText('Down 9.1%')).toBeInTheDocument();
		});

		it('should render "Never" when the product was never checked', () => {
			render(ProductTable, { props: { products: [{ ...mockProduct, lastChecked: undefined }] } });

			expect(getDesktopTable().getByText('Never')).toBeInTheDocument();
		});

		it('should render "No price" when there is no current price', () => {
			render(ProductTable, { props: { products: [{ ...mockProduct, currentPrice: undefined }] } });

			expect(getDesktopTable().getByText('No price')).toBeInTheDocument();
		});

		it('should show an initial instead of a placeholder when there is no image', () => {
			render(ProductTable, { props: { products: [{ ...mockProduct, imageUrl: undefined }] } });
			const table = getDesktopTable();

			expect(table.getByTestId('product-initial')).toHaveTextContent('T');
			expect(table.queryByText('N/A')).not.toBeInTheDocument();
		});

		it('should name the store that gives the price and count the others', () => {
			const product = { ...mockProduct, bestStoreName: 'The Shop', storeCount: 3 };
			render(ProductTable, { props: { products: [product] } });

			expect(getDesktopTable().getByTestId('product-store')).toHaveTextContent('The Shop · 2 more');
		});

		it('should fall back to the host of the URL when the store has no name', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(getDesktopTable().getByTestId('product-store')).toHaveTextContent('example.com');
		});

		it('should display tags when present', () => {
			const productWithTags = {
				...mockProduct,
				tags: [
					{ id: 'tag-1', name: 'Electronics', color: '#3B82F6' },
					{ id: 'tag-2', name: 'Sale', color: '#EF4444' }
				]
			};
			render(ProductTable, { props: { products: [productWithTags] } });
			const table = getDesktopTable();

			expect(table.getByText('Electronics')).toBeInTheDocument();
			expect(table.getByText('Sale')).toBeInTheDocument();
		});

		it('should render the sparkline and the range bar when the data is there', () => {
			const product = {
				...mockProduct,
				sparkline: [
					{ date: '2024-01-01', price: 110 },
					{ date: '2024-01-08', price: 105 },
					{ date: '2024-01-15', price: 99.99 }
				],
				priceMin: 90,
				priceMax: 120
			};
			render(ProductTable, { props: { products: [product] } });
			const table = getDesktopTable();

			expect(table.getByTestId('price-range-bar')).toBeInTheDocument();
			expect(screen.getByTestId('product-table').querySelector('svg polyline')).not.toBeNull();
		});
	});

	describe('Status', () => {
		it('should leave the status empty for a healthy product', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(getDesktopTable().queryByTestId('attention-pill')).not.toBeInTheDocument();
		});

		it('should show the attention pill and the failed runs for a failing product', () => {
			const product: Product = {
				...mockProduct,
				attention: { kind: 'failing', isAction: true, productUrlId: 'u1', url: mockProduct.url, consecutiveFailures: 3 }
			};
			render(ProductTable, { props: { products: [product] } });
			const table = getDesktopTable();

			expect(table.getByTestId('attention-pill')).toHaveTextContent('Failing');
			expect(table.getByTestId('failed-runs')).toHaveTextContent('3 failed runs');
		});

		it('should not count failed runs for an out-of-stock product', () => {
			const product: Product = {
				...mockProduct,
				attention: { kind: 'outOfStock', isAction: false, productUrlId: 'u1', url: mockProduct.url, consecutiveFailures: 2 }
			};
			render(ProductTable, { props: { products: [product] } });

			expect(getDesktopTable().queryByTestId('failed-runs')).not.toBeInTheDocument();
		});

		it('should grey out a price that was last read more than 48 hours ago', () => {
			const product = { ...mockProduct, lastSucceededAt: new Date(Date.now() - 72 * 3_600_000).toISOString() };
			render(ProductTable, { props: { products: [product] } });
			const table = getDesktopTable();

			expect(table.getByTestId('old-price')).toHaveTextContent('old price');
			expect(table.getByTestId('product-price').className).toContain('text-gray-400');
		});

		it('should not mark a recent price as old', () => {
			const product = { ...mockProduct, lastSucceededAt: new Date().toISOString() };
			render(ProductTable, { props: { products: [product] } });

			expect(getDesktopTable().queryByTestId('old-price')).not.toBeInTheDocument();
		});
	});

	describe('Alert column', () => {
		it('should show the closest target and its distance', () => {
			const product: Product = {
				...mockProduct,
				closestAlert: { alertId: 'a1', condition: 'below', targetPrice: 95, gap: 4.99, isNear: true }
			};
			render(ProductTable, { props: { products: [product] } });
			const cell = getDesktopTable().getByTestId('product-alert');

			expect(cell).toHaveTextContent('≤ $95.00');
			expect(cell).toHaveTextContent('$4.99 away');
		});

		it('should say reached when the price is past the target', () => {
			const product: Product = {
				...mockProduct,
				closestAlert: { alertId: 'a1', condition: 'below', targetPrice: 100, gap: -0.01, isNear: true }
			};
			render(ProductTable, { props: { products: [product] } });

			expect(getDesktopTable().getByTestId('product-alert')).toHaveTextContent('Reached');
		});
	});

	describe('Sorting', () => {
		it('should mark the sorted column with aria-sort', () => {
			render(ProductTable, { props: { products: [mockProduct], sortBy: 'price', sortDirection: 'desc', onSortChange: vi.fn() } });
			const table = getDesktopTable();

			expect(table.getByTestId('sort-header-price').closest('th')).toHaveAttribute('aria-sort', 'descending');
			expect(table.getByTestId('sort-header-name').closest('th')).toHaveAttribute('aria-sort', 'none');
		});

		it('should flip the direction of the sorted column', async () => {
			const onSortChange = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], sortBy: 'price', sortDirection: 'asc', onSortChange } });

			await fireEvent.click(getDesktopTable().getByTestId('sort-header-price'));

			expect(onSortChange).toHaveBeenCalledWith('price', 'desc');
		});

		it('should sort a new column in its natural direction', async () => {
			const onSortChange = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], sortBy: 'name', sortDirection: 'asc', onSortChange } });

			await fireEvent.click(getDesktopTable().getByTestId('sort-header-lastchecked'));

			expect(onSortChange).toHaveBeenCalledWith('lastchecked', 'desc');
		});

		it('should render plain headers without a sort handler', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(getDesktopTable().queryByTestId('sort-header-price')).not.toBeInTheDocument();
		});
	});

	describe('Pending Product', () => {
		const pendingProduct: Product = {
			...mockProduct,
			status: 'pending',
			currentPrice: undefined,
			imageUrl: undefined
		};

		it('should render the pending placeholders', () => {
			render(ProductTable, { props: { products: [pendingProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Loading product data...')).toBeInTheDocument();
			expect(table.getByText('Fetching...')).toBeInTheDocument();
			expect(table.getByText('Pending')).toBeInTheDocument();
		});

		it('should not offer an alert or the history for a pending product', async () => {
			render(ProductTable, { props: { products: [pendingProduct], onViewHistory: vi.fn() } });

			await fireEvent.click(getDesktopTable().getByRole('button', { name: 'Actions for Test Headphones' }));

			expect(screen.queryByRole('menuitem', { name: 'Set price alert' })).not.toBeInTheDocument();
			expect(screen.queryByRole('menuitem', { name: 'Price history' })).not.toBeInTheDocument();
		});
	});

	describe('Favourite Toggle', () => {
		it('should not render star button when onFavouriteToggle is not provided', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(getDesktopTable().queryByLabelText('Add Test Headphones to favourites')).not.toBeInTheDocument();
		});

		it('should call onFavouriteToggle with true when adding to favourites', async () => {
			const onFavouriteToggle = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onFavouriteToggle } });

			await fireEvent.click(getDesktopTable().getByLabelText('Add Test Headphones to favourites'));

			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', true);
		});

		it('should call onFavouriteToggle with false when removing from favourites', async () => {
			const onFavouriteToggle = vi.fn();
			render(ProductTable, { props: { products: [{ ...mockProduct, isFavourite: true }], onFavouriteToggle } });

			await fireEvent.click(getDesktopTable().getByLabelText('Remove Test Headphones from favourites'));

			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', false);
		});
	});

	describe('Row actions', () => {
		it('should open the URL that gives the price in a new tab', async () => {
			render(ProductTable, { props: { products: [{ ...mockProduct, bestUrl: 'https://shop.example/hp' }] } });

			await fireEvent.click(getDesktopTable().getByRole('button', { name: 'Actions for Test Headphones' }));

			const link = screen.getByRole('menuitem', { name: 'Open in store' });
			expect(link).toHaveAttribute('href', 'https://shop.example/hp');
			expect(link).toHaveAttribute('target', '_blank');
		});

		it('should call onDelete from the menu', async () => {
			const onDelete = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onDelete } });

			await chooseAction('Test Headphones', 'Delete');

			expect(onDelete).toHaveBeenCalledWith('product-123');
		});

		it('should not offer delete when onDelete is not provided', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			await fireEvent.click(getDesktopTable().getByRole('button', { name: 'Actions for Test Headphones' }));

			expect(screen.queryByRole('menuitem', { name: 'Delete' })).not.toBeInTheDocument();
		});

		it('should call onViewHistory from the menu', async () => {
			const onViewHistory = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onViewHistory } });

			await chooseAction('Test Headphones', 'Price history');

			expect(onViewHistory).toHaveBeenCalledWith(mockProduct);
		});

		it('should pause an active product and resume a paused one', async () => {
			const onStatusChange = vi.fn();
			render(ProductTable, {
				props: { products: [mockProduct, { ...mockProduct2, status: 'paused' as const }], onStatusChange }
			});

			await chooseAction('Test Headphones', 'Pause tracking');
			await chooseAction('Test Keyboard', 'Resume tracking');

			expect(onStatusChange).toHaveBeenCalledWith('product-123', 'paused');
			expect(onStatusChange).toHaveBeenCalledWith('product-456', 'active');
		});
	});

	describe('Quick Alert', () => {
		it('should show quick alert form from the menu', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await chooseAction('Test Headphones', 'Set price alert');

			expect(table.getByText('Alert below')).toBeInTheDocument();
			expect(table.getByLabelText('Create alert')).toBeInTheDocument();
			expect(table.getByRole('region', { name: /Quick price alert for Test Headphones/ })).toBeInTheDocument();
		});

		it('should pre-fill price at 90% of current price', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			await chooseAction('Test Headphones', 'Set price alert');

			const input = getDesktopTable().getByRole('spinbutton') as HTMLInputElement;
			expect(parseFloat(input.value)).toBe(89.99);
		});

		it('should call api.createAlert and show success', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await chooseAction('Test Headphones', 'Set price alert');
			await fireEvent.click(table.getByLabelText('Create alert'));

			expect(api.createAlert).toHaveBeenCalledWith('product-123', 89.99, 'below');
			await waitFor(() => {
				expect(table.getByText('Alert set ✓')).toBeInTheDocument();
				expect(table.getByText('Alert set ✓')).toHaveClass('text-success');
			});
		});

		it('should show error message on failure', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(new Error('Network error'));
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await chooseAction('Test Headphones', 'Set price alert');
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByRole('alert')).toHaveTextContent('Network error');
			});
		});

		it('should show field-level error for ApiError with field details', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(
				new ApiError('VALIDATION_ERROR', 'Validation failed', { targetprice: ['Must be positive'] })
			);
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await chooseAction('Test Headphones', 'Set price alert');
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByText('Must be positive')).toBeInTheDocument();
			});
			expect(table.queryByLabelText('Retry creating alert')).not.toBeInTheDocument();
		});

		it('should show retry button for network errors', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(new ApiError('UnknownError', 'An error occurred'));
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await chooseAction('Test Headphones', 'Set price alert');
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByLabelText('Retry creating alert')).toBeInTheDocument();
			});
		});

		it('should close form when cancel clicked', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await chooseAction('Test Headphones', 'Set price alert');
			await fireEvent.click(table.getByLabelText('Cancel'));

			expect(table.queryByText('Alert below')).not.toBeInTheDocument();
		});
	});

	describe('Mobile view', () => {
		it('should render one mobile card per product', () => {
			render(ProductTable, { props: { products: [mockProduct, mockProduct2] } });

			const mobileContainer = screen.getByTestId('product-table-mobile');
			expect(within(mobileContainer).getAllByTestId('mobile-product-card')).toHaveLength(2);
		});
	});

	describe('Loading State', () => {
		it('should render skeletons for both layouts when loading', () => {
			render(ProductTable, { props: { products: [], loading: true } });

			const mobileContainer = screen.getByTestId('product-table-mobile');
			expect(within(mobileContainer).getAllByTestId('mobile-card-skeleton')).toHaveLength(3);
			expect(within(mobileContainer).queryByTestId('mobile-product-card')).not.toBeInTheDocument();
			expect(getDesktopTable().getAllByTestId('desktop-row-skeleton')).toHaveLength(5);
		});

		it('should not render skeletons when not loading', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(screen.queryByTestId('mobile-card-skeleton')).not.toBeInTheDocument();
			expect(screen.queryByTestId('desktop-row-skeleton')).not.toBeInTheDocument();
		});
	});

	describe('Accessibility', () => {
		it('should have scope="col" on all table headers', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			screen
				.getByTestId('product-table')
				.querySelectorAll('th')
				.forEach((th) => expect(th).toHaveAttribute('scope', 'col'));
		});

		it('should name each row menu after its product', () => {
			render(ProductTable, { props: { products: [mockProduct, mockProduct2] } });
			const table = getDesktopTable();

			expect(table.getByRole('button', { name: 'Actions for Test Headphones' })).toBeInTheDocument();
			expect(table.getByRole('button', { name: 'Actions for Test Keyboard' })).toBeInTheDocument();
		});
	});

	describe('Null Safety', () => {
		it('should render without errors when product.tags is undefined', () => {
			const productNoTags = { ...mockProduct, tags: undefined as unknown as Product['tags'] };
			render(ProductTable, { props: { products: [productNoTags] } });

			expect(getDesktopTable().getByText('Test Headphones')).toBeInTheDocument();
		});
	});

	describe('Density', () => {
		it('should use comfortable name sizing by default', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const link = getDesktopTable().getByText('Test Headphones');
			expect(link.className).toContain('text-base');
			expect(link.className).not.toContain('text-sm');
		});

		it('should apply compact sizing when density is compact', () => {
			density.set('compact');
			render(ProductTable, { props: { products: [mockProduct] } });
			const link = getDesktopTable().getByText('Test Headphones');
			expect(link.className).toContain('text-sm');
			expect(screen.getByTestId('product-table').querySelector('img')?.className).toContain('w-7 h-7');
		});
	});
});
