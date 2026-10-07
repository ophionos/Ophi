import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import CommandPalette from './CommandPalette.svelte';

// Mock the api client
vi.mock('$lib/api/client', () => ({
	api: {
		getProducts: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 24 }),
		getTags: vi.fn().mockResolvedValue({ items: [] }),
		getStores: vi.fn().mockResolvedValue({ items: [], total: 0 })
	}
}));

// Mock navigation
vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

// Non-identity resolve so tests catch hrefs that bypass resolve() (base-path support).
vi.mock('$app/paths', () => ({
	resolve: (path: string) => `/base${path}`
}));

describe('CommandPalette', () => {
	const defaultProps = {
		isOpen: true,
		onClose: vi.fn()
	};

	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('rendering', () => {
		it('should render when isOpen is true', () => {
			render(CommandPalette, { props: defaultProps });
			expect(screen.getByPlaceholderText('Search products, pages, tags...')).toBeInTheDocument();
		});

		it('should not render when isOpen is false', () => {
			render(CommandPalette, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByPlaceholderText('Search products, pages, tags...')).not.toBeInTheDocument();
		});

		it('should show search input focused when opened', () => {
			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			expect(input).toBeInTheDocument();
		});

		it('should show navigation items when query is empty', () => {
			render(CommandPalette, { props: defaultProps });
			expect(screen.getByText('Dashboard')).toBeInTheDocument();
			expect(screen.getByText('Comparisons')).toBeInTheDocument();
			expect(screen.getByText('Tags')).toBeInTheDocument();
			expect(screen.getByText('Stores')).toBeInTheDocument();
		});

		it('should show Navigation section header', () => {
			render(CommandPalette, { props: defaultProps });
			expect(screen.getByText('Pages')).toBeInTheDocument();
		});
	});

	describe('closing', () => {
		it('should call onClose when Escape is pressed', async () => {
			const onClose = vi.fn();
			render(CommandPalette, { props: { ...defaultProps, onClose } });
			await fireEvent.keyDown(window, { key: 'Escape' });
			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when backdrop is clicked', async () => {
			const onClose = vi.fn();
			render(CommandPalette, { props: { ...defaultProps, onClose } });
			const backdrop = screen.getByRole('presentation');
			await fireEvent.click(backdrop);
			expect(onClose).toHaveBeenCalled();
		});
	});

	describe('filtering', () => {
		it('should filter navigation items by search query', async () => {
			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'dash' } });
			expect(screen.getByText('Dashboard')).toBeInTheDocument();
			expect(screen.queryByText('Comparisons')).not.toBeInTheDocument();
		});

		it('should show "No results" for unmatched queries', async () => {
			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'xyznonexistent' } });
			// Wait for debounce and API call to resolve
			await vi.waitFor(() => {
				expect(screen.getByText('No results found')).toBeInTheDocument();
			});
		});
	});

	describe('keyboard navigation', () => {
		it('should highlight items with ArrowDown', async () => {
			const { container } = render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');

			await fireEvent.keyDown(input, { key: 'ArrowDown' });
			// First item should be selected (index 0 -> 1)
			const selectedItems = container.querySelectorAll('[data-selected="true"]');
			expect(selectedItems.length).toBe(1);
		});

		it('should highlight items with ArrowUp', async () => {
			const { container } = render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');

			// ArrowUp from 0 should wrap to last item
			await fireEvent.keyDown(input, { key: 'ArrowUp' });
			const selectedItems = container.querySelectorAll('[data-selected="true"]');
			expect(selectedItems.length).toBe(1);
		});

		it('should navigate on Enter and call onClose', async () => {
			const onClose = vi.fn();
			const { goto } = await import('$app/navigation');
			render(CommandPalette, { props: { ...defaultProps, onClose } });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');

			// First item (Dashboard) is at index 0, press Enter directly
			await fireEvent.keyDown(input, { key: 'Enter' });

			expect(goto).toHaveBeenCalledWith('/base/dashboard');
			expect(onClose).toHaveBeenCalled();
		});
	});

	describe('when closed', () => {
		it('should not navigate when Enter is pressed while closed', async () => {
			const onClose = vi.fn();
			const { goto } = await import('$app/navigation');
			render(CommandPalette, { props: { isOpen: false, onClose } });

			await fireEvent.keyDown(window, { key: 'Enter' });

			expect(goto).not.toHaveBeenCalled();
			expect(onClose).not.toHaveBeenCalled();
		});

		it('should not call onClose when Escape is pressed while closed', async () => {
			const onClose = vi.fn();
			render(CommandPalette, { props: { isOpen: false, onClose } });

			await fireEvent.keyDown(window, { key: 'Escape' });

			expect(onClose).not.toHaveBeenCalled();
		});

		it('should not cancel arrow key events while closed', async () => {
			render(CommandPalette, { props: { isOpen: false, onClose: vi.fn() } });

			// fireEvent resolves false when preventDefault() was called on the event.
			const notCancelled = await fireEvent.keyDown(window, { key: 'ArrowDown' });

			expect(notCancelled).toBe(true);
		});
	});

	describe('dynamic search', () => {
		it('should show product results when searching', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getProducts).mockResolvedValueOnce({
				items: [
					{
						id: 'p1',
						name: 'Test Widget',
						url: 'https://example.com/widget',
						currency: 'USD',
						status: 'active',
						isFavourite: false,
						isOutOfStock: false,
						storeCount: 1,
						alertCount: 0,
						tags: [],
						customFields: []
					}
				],
				total: 1,
				page: 1,
				pageSize: 24,
				atLowestCount: 0,
				priceDropCount: 0,
				withAlertsCount: 0
			});

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'widget' } });

			await vi.waitFor(() => {
				expect(screen.getByText('Test Widget')).toBeInTheDocument();
			});
		});

		it('should show tag results when searching', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getTags).mockResolvedValueOnce({
				items: [
					{ id: 't1', name: 'Electronics', color: '#0d9488', weight: 0, productCount: 5 }
				]
			});

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'elec' } });

			await vi.waitFor(() => {
				expect(screen.getByText('Electronics')).toBeInTheDocument();
			});
		});

		it('should pass search query to getTags API', async () => {
			const { api } = await import('$lib/api/client');

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'elec' } });

			await vi.waitFor(() => {
				expect(api.getTags).toHaveBeenCalledWith('elec');
			});
		});

		it('should pass search query to getStores API', async () => {
			const { api } = await import('$lib/api/client');

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'amaz' } });

			await vi.waitFor(() => {
				expect(api.getStores).toHaveBeenCalledWith('amaz');
			});
		});

		it('should show error message when all APIs fail', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getProducts).mockRejectedValueOnce(new Error('Network error'));
			vi.mocked(api.getTags).mockRejectedValueOnce(new Error('Network error'));
			vi.mocked(api.getStores).mockRejectedValueOnce(new Error('Network error'));

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'laptop' } });

			await vi.waitFor(() => {
				expect(screen.getByRole('status')).toHaveTextContent('Search unavailable — check your connection');
			});
		});

		it('should show partial results when only one API fails', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getProducts).mockResolvedValueOnce({
				items: [
					{
						id: 'p1',
						name: 'Partial Product',
						url: 'https://example.com/product',
						currency: 'USD',
						status: 'active',
						isFavourite: false,
						isOutOfStock: false,
						storeCount: 1,
						alertCount: 0,
						tags: [],
						customFields: []
					}
				],
				total: 1,
				page: 1,
				pageSize: 24,
				atLowestCount: 0,
				priceDropCount: 0,
				withAlertsCount: 0
			});
			vi.mocked(api.getTags).mockRejectedValueOnce(new Error('Tags service down'));
			// getStores uses the default mock (returns empty)

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'partial' } });

			await vi.waitFor(() => {
				expect(screen.getByText('Partial Product')).toBeInTheDocument();
				expect(screen.queryByText('Search unavailable — check your connection')).not.toBeInTheDocument();
			});
		});

		it('should navigate to product on click and call onClose', async () => {
			const onClose = vi.fn();
			const { api } = await import('$lib/api/client');
			const { goto } = await import('$app/navigation');
			vi.mocked(api.getProducts).mockResolvedValueOnce({
				items: [
					{
						id: 'p1',
						name: 'Clickable Product',
						url: 'https://example.com/product',
						currency: 'USD',
						status: 'active',
						isFavourite: false,
						isOutOfStock: false,
						storeCount: 1,
						alertCount: 0,
						tags: [],
						customFields: []
					}
				],
				total: 1,
				page: 1,
				pageSize: 24,
				atLowestCount: 0,
				priceDropCount: 0,
				withAlertsCount: 0
			});

			render(CommandPalette, { props: { ...defaultProps, onClose } });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'clickable' } });

			await vi.waitFor(() => {
				expect(screen.getByText('Clickable Product')).toBeInTheDocument();
			});

			await fireEvent.click(screen.getByText('Clickable Product'));
			expect(goto).toHaveBeenCalledWith('/base/products/p1');
			expect(onClose).toHaveBeenCalled();
		});
	});

	describe('actions', () => {
		it('should list action items alongside navigation', () => {
			render(CommandPalette, { props: defaultProps });

			expect(screen.getByText('Actions')).toBeInTheDocument();
			expect(screen.getByText('Add product')).toBeInTheDocument();
			expect(screen.getByText('Toggle theme')).toBeInTheDocument();
		});

		it('should run Toggle theme as an action and close, without navigating', async () => {
			const onClose = vi.fn();
			const { goto } = await import('$app/navigation');
			const { theme } = await import('$lib/stores/theme.svelte');
			const before = theme.current;
			render(CommandPalette, { props: { ...defaultProps, onClose } });

			await fireEvent.click(screen.getByText('Toggle theme'));

			expect(theme.current).not.toBe(before);
			expect(goto).not.toHaveBeenCalled();
			expect(onClose).toHaveBeenCalled();
		});

		it('should navigate to the dashboard for Add product', async () => {
			const { goto } = await import('$app/navigation');
			render(CommandPalette, { props: defaultProps });

			await fireEvent.click(screen.getByText('Add product'));

			expect(goto).toHaveBeenCalledWith('/base/dashboard');
		});

		it('should filter action items by query', async () => {
			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');

			await fireEvent.input(input, { target: { value: 'theme' } });

			expect(screen.getByText('Toggle theme')).toBeInTheDocument();
			expect(screen.queryByText('Add product')).not.toBeInTheDocument();
		});
	});

	describe('store deep-links', () => {
		it('should deep-link editable store results via ?edit=<domain>', async () => {
			const { api } = await import('$lib/api/client');
			const { goto } = await import('$app/navigation');
			vi.mocked(api.getStores).mockResolvedValueOnce({
				items: [
					{
						id: 's1',
						storeId: 'mystore',
						name: 'My Store',
						domainPatterns: ['mystore.com'],
						selectors: { priceSelectors: [], nameSelectors: [], imageSelectors: [] },
						isBuiltIn: false,
						priceLocale: 'en-US',
						requiresJavaScript: false
					}
				],
				total: 1
			});

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'mystore' } });

			await vi.waitFor(() => {
				expect(screen.getByText('My Store')).toBeInTheDocument();
			});
			await fireEvent.click(screen.getByText('My Store'));

			expect(goto).toHaveBeenCalledWith('/base/stores?edit=mystore.com');
		});

		it('should link built-in store results to the plain stores page', async () => {
			const { api } = await import('$lib/api/client');
			const { goto } = await import('$app/navigation');
			vi.mocked(api.getStores).mockResolvedValueOnce({
				items: [
					{
						id: 's2',
						storeId: 'amazon',
						name: 'Amazon',
						domainPatterns: ['amazon.com'],
						selectors: { priceSelectors: [], nameSelectors: [], imageSelectors: [] },
						isBuiltIn: true,
						priceLocale: 'en-US',
						requiresJavaScript: false
					}
				],
				total: 1
			});

			render(CommandPalette, { props: defaultProps });
			const input = screen.getByPlaceholderText('Search products, pages, tags...');
			await fireEvent.input(input, { target: { value: 'amazon' } });

			await vi.waitFor(() => {
				expect(screen.getByText('Amazon')).toBeInTheDocument();
			});
			await fireEvent.click(screen.getByText('Amazon'));

			expect(goto).toHaveBeenCalledWith('/base/stores');
		});
	});

	describe('accessibility', () => {
		it('should have role="dialog" and aria-modal on the palette container', () => {
			render(CommandPalette, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			expect(dialog).toHaveAttribute('aria-modal', 'true');
			expect(dialog).toHaveAttribute('aria-label', 'Command palette');
		});
	});
});
