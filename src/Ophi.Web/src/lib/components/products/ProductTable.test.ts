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

describe('ProductTable', () => {
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
		it('should render table with column headers', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Product')).toBeInTheDocument();
			expect(table.getByText('Price')).toBeInTheDocument();
			expect(table.getByText('Status')).toBeInTheDocument();
			expect(table.getByText('Updated')).toBeInTheDocument();
			expect(table.getByText('Actions')).toBeInTheDocument();
		});

		it('should render product rows with name', () => {
			render(ProductTable, { props: { products: [mockProduct, mockProduct2] } });
			const table = getDesktopTable();

			expect(table.getByText('Test Headphones')).toBeInTheDocument();
			expect(table.getByText('Test Keyboard')).toBeInTheDocument();
		});

		it('should render current price', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('$99.99')).toBeInTheDocument();
		});

		it('should render the price with tabular figures so the column aligns', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('$99.99').className).toContain('tabular-nums');
		});

		it('should render price change percentage', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('9.1%')).toBeInTheDocument();
		});

		it('should render status badge', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Active')).toBeInTheDocument();
		});

		it('should render last checked date', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			const dateCell = table.getByText((content) => {
				return content.includes('1/15/2024') || content.includes('15/01/2024') || content.includes('2024');
			});
			expect(dateCell).toBeInTheDocument();
		});

		it('should render product image when imageUrl is provided', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			const img = table.getByRole('img');
			expect(img).toHaveAttribute('src', 'https://example.com/image.jpg');
		});

		it('should render external link with correct href', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			const externalLink = table.getByLabelText('Open product in new tab');
			expect(externalLink).toHaveAttribute('href', 'https://example.com/product');
			expect(externalLink).toHaveAttribute('target', '_blank');
		});

		it('should render "Never checked" when lastChecked is not provided', () => {
			const product = { ...mockProduct, lastChecked: undefined };
			render(ProductTable, { props: { products: [product] } });
			const table = getDesktopTable();

			expect(table.getByText('Never checked')).toBeInTheDocument();
		});

		it('should render "Price unavailable" when currentPrice is not set', () => {
			const product = { ...mockProduct, currentPrice: undefined };
			render(ProductTable, { props: { products: [product] } });
			const table = getDesktopTable();

			expect(table.getByText('Price unavailable')).toBeInTheDocument();
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
			render(ProductTable, { props: { products: [pendingProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Loading product data...')).toBeInTheDocument();
		});

		it('should render "Fetching..." for price', () => {
			render(ProductTable, { props: { products: [pendingProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Fetching...')).toBeInTheDocument();
		});

		it('should render Pending status badge', () => {
			render(ProductTable, { props: { products: [pendingProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Pending')).toBeInTheDocument();
		});

		it('should not render quick alert button for pending products', () => {
			render(ProductTable, { props: { products: [pendingProduct] } });
			const table = getDesktopTable();

			expect(table.queryByLabelText('Set quick price alert')).not.toBeInTheDocument();
		});

		it('should not render history button for pending products', () => {
			const onViewHistory = vi.fn();
			render(ProductTable, { props: { products: [pendingProduct], onViewHistory } });
			const table = getDesktopTable();

			expect(table.queryByLabelText('View price history')).not.toBeInTheDocument();
		});
	});

	describe('Favourite Toggle', () => {
		it('should not render star button when onFavouriteToggle is not provided', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.queryByLabelText('Add to favourites')).not.toBeInTheDocument();
		});

		it('should render star button when onFavouriteToggle is provided', () => {
			const onFavouriteToggle = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onFavouriteToggle } });
			const table = getDesktopTable();

			expect(table.getByLabelText('Add to favourites')).toBeInTheDocument();
		});

		it('should call onFavouriteToggle with true when adding to favourites', async () => {
			const onFavouriteToggle = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onFavouriteToggle } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Add to favourites'));

			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', true);
		});

		it('should call onFavouriteToggle with false when removing from favourites', async () => {
			const onFavouriteToggle = vi.fn();
			const favouriteProduct = { ...mockProduct, isFavourite: true };
			render(ProductTable, { props: { products: [favouriteProduct], onFavouriteToggle } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Remove from favourites'));

			expect(onFavouriteToggle).toHaveBeenCalledWith('product-123', false);
		});
	});

	describe('Callbacks', () => {
		it('should call onDelete when delete button is clicked', async () => {
			const onDelete = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onDelete } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Delete product'));

			expect(onDelete).toHaveBeenCalledWith('product-123');
		});

		it('should not render delete button when onDelete is not provided', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.queryByLabelText('Delete product')).not.toBeInTheDocument();
		});

		it('should call onViewHistory when history button is clicked', async () => {
			const onViewHistory = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onViewHistory } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('View price history'));

			expect(onViewHistory).toHaveBeenCalledWith(mockProduct);
		});

		it('should call onStatusChange when pause button is clicked', async () => {
			const onStatusChange = vi.fn();
			render(ProductTable, { props: { products: [mockProduct], onStatusChange } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Pause tracking'));

			expect(onStatusChange).toHaveBeenCalledWith('product-123', 'paused');
		});

		it('should call onStatusChange with active when resume button is clicked', async () => {
			const onStatusChange = vi.fn();
			const pausedProduct = { ...mockProduct, status: 'paused' as const };
			render(ProductTable, { props: { products: [pausedProduct], onStatusChange } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Resume tracking'));

			expect(onStatusChange).toHaveBeenCalledWith('product-123', 'active');
		});
	});

	describe('Store Count Badge', () => {
		it('should show store count for multiple stores', () => {
			const multiStoreProduct = { ...mockProduct, storeCount: 3 };
			render(ProductTable, { props: { products: [multiStoreProduct] } });
			const table = getDesktopTable();

			expect(table.getByTestId('store-count-badge')).toHaveTextContent('3');
		});

		it('should show store count for single store', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByTestId('store-count-badge')).toHaveTextContent('1');
		});

		it('should show alert count badge when alertCount > 0', () => {
			const productWithAlerts = { ...mockProduct, alertCount: 2 };
			render(ProductTable, { props: { products: [productWithAlerts] } });
			const table = getDesktopTable();

			expect(table.getByTestId('alert-count-badge')).toBeInTheDocument();
		});

		it('should not show alert count badge when alertCount is 0', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.queryByTestId('alert-count-badge')).not.toBeInTheDocument();
		});
	});

	describe('Tags', () => {
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
	});

	describe('Quick Alert', () => {
		it('should show quick alert form when bell clicked', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));

			expect(table.getByText('Alert below')).toBeInTheDocument();
			expect(table.getByLabelText('Create alert')).toBeInTheDocument();
			expect(table.getByLabelText('Cancel')).toBeInTheDocument();
		});

		it('should pre-fill price at 90% of current price', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));

			const input = table.getByRole('spinbutton') as HTMLInputElement;
			expect(parseFloat(input.value)).toBe(89.99);
		});

		it('should call api.createAlert on submit', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));
			await fireEvent.click(table.getByLabelText('Create alert'));

			expect(api.createAlert).toHaveBeenCalledWith('product-123', 89.99, 'below');
		});

		it('should show success message after alert created', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByText('Alert set ✓')).toBeInTheDocument();
			});
		});

		it('should show error message on failure', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(new Error('Network error'));

			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByRole('alert')).toHaveTextContent('Network error');
			});
		});

		it('should close form when cancel clicked', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));
			expect(table.getByText('Alert below')).toBeInTheDocument();

			await fireEvent.click(table.getByLabelText('Cancel'));
			expect(table.queryByText('Alert below')).not.toBeInTheDocument();
		});
	});

	describe('Mobile view', () => {
		it('should render mobile card stack', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(screen.getByTestId('product-table-mobile')).toBeInTheDocument();
		});

		it('should render one mobile card per product', () => {
			render(ProductTable, { props: { products: [mockProduct, mockProduct2] } });

			const mobileContainer = screen.getByTestId('product-table-mobile');
			const cards = within(mobileContainer).getAllByTestId('mobile-product-card');
			expect(cards).toHaveLength(2);
		});
	});

	describe('Loading State', () => {
		it('should render mobile skeleton cards when loading', () => {
			render(ProductTable, { props: { products: [], loading: true } });

			const mobileContainer = screen.getByTestId('product-table-mobile');
			const skeletons = within(mobileContainer).getAllByTestId('mobile-card-skeleton');
			expect(skeletons).toHaveLength(3);
		});

		it('should render desktop skeleton rows when loading', () => {
			render(ProductTable, { props: { products: [], loading: true } });

			const table = getDesktopTable();
			const skeletons = table.getAllByTestId('desktop-row-skeleton');
			expect(skeletons).toHaveLength(5);
		});

		it('should not render skeletons when not loading', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			expect(screen.queryByTestId('mobile-card-skeleton')).not.toBeInTheDocument();
			expect(screen.queryByTestId('desktop-row-skeleton')).not.toBeInTheDocument();
		});

		it('should not render product rows in mobile when loading', () => {
			render(ProductTable, { props: { products: [], loading: true } });

			const mobileContainer = screen.getByTestId('product-table-mobile');
			expect(within(mobileContainer).queryByTestId('mobile-product-card')).not.toBeInTheDocument();
		});
	});

	describe('Quick Alert Error Differentiation', () => {
		it('should show field-level error for ApiError with field details', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(
				new ApiError('VALIDATION_ERROR', 'Validation failed', { targetprice: ['Must be positive'] })
			);

			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByText('Must be positive')).toBeInTheDocument();
			});
			expect(table.queryByLabelText('Retry creating alert')).not.toBeInTheDocument();
		});

		it('should show retry button for network errors', async () => {
			vi.mocked(api.createAlert).mockRejectedValueOnce(
				new ApiError('UnknownError', 'An error occurred')
			);

			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));
			await fireEvent.click(table.getByLabelText('Create alert'));

			await waitFor(() => {
				expect(table.getByLabelText('Retry creating alert')).toBeInTheDocument();
			});
		});
	});

	describe('Accessibility', () => {
		it('should have scope="col" on all table headers', () => {
			render(ProductTable, { props: { products: [mockProduct] } });

			const tableEl = screen.getByTestId('product-table');
			const headers = tableEl.querySelectorAll('th');
			headers.forEach((th) => {
				expect(th).toHaveAttribute('scope', 'col');
			});
		});

		it('should have sr-only label for favourite column header', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			expect(table.getByText('Favourite')).toBeInTheDocument();
		});

		it('should have aria-label on no-image placeholder', () => {
			const productWithoutImage = { ...mockProduct, imageUrl: undefined };
			render(ProductTable, { props: { products: [productWithoutImage] } });
			const table = getDesktopTable();

			expect(table.getByRole('img', { name: 'No image available' })).toBeInTheDocument();
		});

		it('should have aria-labels on all action buttons', () => {
			const onDelete = vi.fn();
			const onViewHistory = vi.fn();
			const onStatusChange = vi.fn();
			render(ProductTable, {
				props: { products: [mockProduct], onDelete, onViewHistory, onStatusChange }
			});
			const table = getDesktopTable();

			expect(table.getByLabelText('Open product in new tab')).toBeInTheDocument();
			expect(table.getByLabelText('View price history')).toBeInTheDocument();
			expect(table.getByLabelText('Pause tracking')).toBeInTheDocument();
			expect(table.getByLabelText('Set quick price alert')).toBeInTheDocument();
			expect(table.getByLabelText('Delete product')).toBeInTheDocument();
		});

		it('should wrap quick-alert in role="region" with aria-label', async () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const table = getDesktopTable();

			await fireEvent.click(table.getByLabelText('Set quick price alert'));

			const region = table.getByRole('region', { name: /Quick price alert for Test Headphones/ });
			expect(region).toBeInTheDocument();
		});
	});

	describe('Null Safety', () => {
		it('should render without errors when product.tags is undefined', () => {
			const productNoTags = { ...mockProduct, tags: undefined as unknown as Product['tags'] };
			render(ProductTable, { props: { products: [productNoTags] } });
			const table = getDesktopTable();

			expect(table.getByText('Test Headphones')).toBeInTheDocument();
		});
	});

	describe('Density', () => {
		it('should use comfortable name sizing by default', () => {
			render(ProductTable, { props: { products: [mockProduct] } });
			const link = getDesktopTable().getByText('Test Headphones');
			expect(link.className).toContain('text-base');
			expect(link.className).not.toContain('text-sm');
		});

		it('should apply compact name sizing when density is compact', () => {
			density.set('compact');
			render(ProductTable, { props: { products: [mockProduct] } });
			const link = getDesktopTable().getByText('Test Headphones');
			expect(link.className).toContain('text-sm');
			expect(link.className).not.toContain('text-base');
		});

		it('should shrink the product thumbnail in compact mode', () => {
			density.set('compact');
			render(ProductTable, { props: { products: [mockProduct] } });
			const img = getDesktopTable().getByAltText('Test Headphones');
			expect(img.className).toContain('w-7 h-7');
		});
	});
});
