import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within, fireEvent } from '@testing-library/svelte';
import ProductDetailPage from '../../../routes/products/[id]/+page.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { api } from '$lib/api/client';
import { page } from '$app/state';
import type { ProductDetail, PriceHistory, Store } from '$lib/api/client';
import type { PageData } from '../../../routes/products/[id]/$types';

// Mock the API client
vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			getProduct: vi.fn(),
			getPriceHistory: vi.fn(),
			getComparisonGroups: vi.fn(),
			getTags: vi.fn(),
			createAlert: vi.fn(),
			redenominateAlert: vi.fn(),
			deleteAlert: vi.fn(),
			setAlertActive: vi.fn(),
			updateProduct: vi.fn(),
			deleteProduct: vi.fn(),
			addProductUrl: vi.fn(),
			removeProductUrl: vi.fn(),
			retryScrapeProductUrl: vi.fn(),
			addProductToComparisonGroup: vi.fn(),
			removeProductFromComparisonGroup: vi.fn(),
			addTagToProduct: vi.fn(),
			removeTagFromProduct: vi.fn(),
			getScrapeLog: vi.fn(),
			getStores: vi.fn()
		}
	};
});

// Mock navigation
vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

// Mock chart.js to avoid canvas issues in jsdom
vi.mock('chart.js', () => {
	function MockChart() {
		return {
			destroy: vi.fn(),
			update: vi.fn()
		};
	}

	return {
		Chart: Object.assign(MockChart, {
			register: vi.fn()
		}),
		LineController: vi.fn(),
		LineElement: vi.fn(),
		PointElement: vi.fn(),
		LinearScale: vi.fn(),
		TimeScale: vi.fn(),
		CategoryScale: vi.fn(),
		Title: vi.fn(),
		Tooltip: vi.fn(),
		Legend: vi.fn(),
		Filler: vi.fn()
	};
});

const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };

const mockProductWithCustomFields: ProductDetail = {
	id: 'p1',
	name: 'MacBook Pro',
	url: 'https://apple.com/macbook',
	imageUrl: 'https://apple.com/macbook.jpg',
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
	tags: [],
	customFields: [
		{ name: 'RAM', value: '16GB' },
		{ name: 'Storage', value: '512GB SSD' },
		{ name: 'Color', value: 'Space Gray' }
	],
	urls: [
		{
			id: 'url-1',
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
	statistics: {
		min: 1899.99,
		max: 2099.99,
		average: 1999.99,
		current: 1999.99
	},
	alerts: []
};

const mockProductNoCustomFields: ProductDetail = {
	...mockProductWithCustomFields,
	customFields: []
};

const mockProductWithCurrencyMismatch: ProductDetail = {
	...mockProductWithCustomFields,
	hasCurrencyMismatch: true,
	customFields: [],
	urls: [
		{
			id: 'url-usd',
			url: 'https://amazon.com/macbook',
			currentPrice: 1999.99,
			currency: 'USD',
			lastCheckedAt: '2025-01-15T12:00:00Z',
			failureCount: 0,
			status: 'active',
			isOutOfStock: false
		},
		{
			id: 'url-gbp',
			url: 'https://amazon.co.uk/macbook',
			currentPrice: 1599.99,
			currency: 'GBP',
			lastCheckedAt: '2025-01-15T12:00:00Z',
			failureCount: 0,
			status: 'active',
			isOutOfStock: false
		}
	]
};

// Stable price across two same-currency stores with sparse history → the single-price card
// renders (not enough history for the stat grid, no currency mismatch) and should show a
// per-store breakdown instead of the "not enough history" fallback.
const mockProductStableMultiStore: ProductDetail = {
	...mockProductWithCustomFields,
	customFields: [],
	currentPrice: 279.99,
	currency: 'EUR',
	hasCurrencyMismatch: false,
	statistics: { min: 279.99, max: 279.99, average: 279.99, current: 279.99 },
	urls: [
		{
			id: 'u-amz',
			url: 'https://amazon.es/p',
			currentPrice: 294.78,
			currency: 'EUR',
			lastCheckedAt: '2025-01-15T12:00:00Z',
			failureCount: 0,
			status: 'active',
			isOutOfStock: false
		},
		{
			id: 'u-wor',
			url: 'https://worten.pt/p',
			currentPrice: 279.99,
			currency: 'EUR',
			lastCheckedAt: '2025-01-15T12:00:00Z',
			failureCount: 0,
			status: 'active',
			isOutOfStock: false
		}
	]
};

const mockPriceHistory: PriceHistory = {
	productId: 'p1',
	productName: 'MacBook Pro',
	history: [
		{ date: '2025-01-01', price: 2099.99 },
		{ date: '2025-01-15', price: 1999.99 }
	],
	hasCurrencyMismatch: false,
	statistics: {
		min: 1899.99,
		max: 2099.99,
		average: 1999.99,
		current: 1999.99
	}
};

function setupPageParams(id: string) {
	page.params = { id };
}

function renderPage(product: ProductDetail | null, stores: Store[] = []) {
	// Action-handler mocks for code paths that hit the API directly
	vi.mocked(api.getProduct).mockResolvedValue(product as ProductDetail);
	vi.mocked(api.getPriceHistory).mockResolvedValue(mockPriceHistory);
	vi.mocked(api.getComparisonGroups).mockResolvedValue({ items: [] });
	vi.mocked(api.getTags).mockResolvedValue({ items: [] });
	vi.mocked(api.getScrapeLog).mockResolvedValue({ items: [] });

	return render(ProductDetailPage, {
		props: {
			data: {
				user: null,
				authChecked: true,
				isPublic: false,
				product,
				priceHistory: product ? mockPriceHistory : null,
				comparisonGroups: [],
				tags: [],
				stores
			} as unknown as PageData
		}
	});
}

describe('Product Detail Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		auth.logout();
		setupPageParams('p1');
	});

	describe('custom fields section', () => {
		it('should display custom fields section when fields exist', async () => {
			auth.login(mockUser);
			const { container } = renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				expect(
					container.querySelector('[data-testid="custom-fields-section"]')
				).toBeInTheDocument();
				expect(screen.getByText('Custom Fields')).toBeInTheDocument();
			});

			// Check field names and values
			expect(screen.getByText('RAM')).toBeInTheDocument();
			expect(screen.getByText('16GB')).toBeInTheDocument();
			expect(screen.getByText('Storage')).toBeInTheDocument();
			expect(screen.getByText('512GB SSD')).toBeInTheDocument();
			expect(screen.getByText('Color')).toBeInTheDocument();
			expect(screen.getByText('Space Gray')).toBeInTheDocument();
		});

		it('should hide custom fields section when no fields', async () => {
			auth.login(mockUser);
			const { container } = renderPage(mockProductNoCustomFields);

			await waitFor(() => {
				expect(screen.getAllByText('MacBook Pro').length).toBeGreaterThanOrEqual(1);
			});

			expect(
				container.querySelector('[data-testid="custom-fields-section"]')
			).not.toBeInTheDocument();
			expect(screen.queryByText('Custom Fields')).not.toBeInTheDocument();
		});

		it('should render custom fields as a table with name-value pairs', async () => {
			auth.login(mockUser);
			const { container } = renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				expect(
					container.querySelector('[data-testid="custom-fields-section"]')
				).toBeInTheDocument();
			});

			const section = container.querySelector('[data-testid="custom-fields-section"]')!;
			const rows = section.querySelectorAll('tr');
			expect(rows).toHaveLength(3);

			// First row: RAM -> 16GB
			const firstRowCells = rows[0].querySelectorAll('td');
			expect(firstRowCells[0].textContent?.trim()).toBe('RAM');
			expect(firstRowCells[1].textContent?.trim()).toBe('16GB');
		});
	});

	describe('currency mismatch', () => {
		it('should show currency mismatch banner when product has mixed currencies', async () => {
			auth.login(mockUser);
			renderPage(mockProductWithCurrencyMismatch);

			await waitFor(() => {
				expect(screen.getByTestId('currency-mismatch-warning')).toBeInTheDocument();
				expect(screen.getByText(/USD, GBP/)).toBeInTheDocument();
				expect(screen.getByText(/Direct comparison may not be accurate/)).toBeInTheDocument();
			});
		});

		it('should show per-URL prices when currencies are mixed', async () => {
			auth.login(mockUser);
			renderPage(mockProductWithCurrencyMismatch);

			await waitFor(() => {
				expect(screen.getByTestId('currency-mismatch-warning')).toBeInTheDocument();
			});

			// Per-URL domain names should be visible (may appear in both statistics cards and Store URLs section)
			expect(screen.getAllByText('amazon.com').length).toBeGreaterThanOrEqual(1);
			expect(screen.getAllByText('amazon.co.uk').length).toBeGreaterThanOrEqual(1);

			// Per-URL prices with their respective currencies should be visible
			// (may appear in both mismatch statistics cards and Store URLs section)
			expect(screen.getAllByText('$1,999.99').length).toBeGreaterThanOrEqual(1);
			expect(screen.getAllByText('£1,599.99').length).toBeGreaterThanOrEqual(1);

			// Aggregate stat labels should NOT be visible (Lowest, Highest, Average)
			expect(screen.queryByText('Lowest')).not.toBeInTheDocument();
			expect(screen.queryByText('Highest')).not.toBeInTheDocument();
			expect(screen.queryByText('Average')).not.toBeInTheDocument();
		});
	});

	describe('current price breakdown', () => {
		it('should show a per-store price breakdown when multiple stores have prices but history is sparse', async () => {
			auth.login(mockUser);
			renderPage(mockProductStableMultiStore);

			const card = await screen.findByTestId('single-price-display');
			// Both stores appear in the breakdown; amazon's price is unique to this card
			expect(within(card).getByText('amazon.es')).toBeInTheDocument();
			expect(within(card).getByText('worten.pt')).toBeInTheDocument();
			expect(within(card).getByText('EUR 294.78')).toBeInTheDocument();
			// The breakdown replaces the "not enough history" fallback copy
			expect(within(card).queryByText('Not enough history for statistics')).not.toBeInTheDocument();
		});

		it('should show the fallback copy when only one store has a price', async () => {
			auth.login(mockUser);
			renderPage({
				...mockProductStableMultiStore,
				urls: [mockProductStableMultiStore.urls[1]]
			});

			const card = await screen.findByTestId('single-price-display');
			expect(within(card).getByText('Not enough history for statistics')).toBeInTheDocument();
		});
	});

	describe('rendering', () => {
		it('should render product name after loading', async () => {
			auth.login(mockUser);
			renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				expect(screen.getAllByText('MacBook Pro').length).toBeGreaterThanOrEqual(1);
			});
		});

		it('should render current price', async () => {
			auth.login(mockUser);
			renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				const priceElements = screen.getAllByText(/1,999\.99/);
				expect(priceElements.length).toBeGreaterThanOrEqual(1);
			});
		});

		it('should render status badge', async () => {
			auth.login(mockUser);
			renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				expect(screen.getByText('active')).toBeInTheDocument();
			});
		});
	});

	describe('store configuration gear', () => {
		const builtInAmazon: Store = {
			id: undefined, // built-in stores have no DB row / editable id
			storeId: 'amazon',
			name: 'Amazon',
			domainPatterns: ['amazon.com'],
			selectors: { priceSelectors: ['.a-price'], nameSelectors: ['#t'], imageSelectors: ['#i'] },
			isBuiltIn: true,
			priceLocale: 'en-US',
			requiresJavaScript: false
		};

		// An amazon.com product URL with NO storeId — mirrors a freshly-added/pending product
		// whose first scrape hasn't stamped a storeId yet. The gear must still be hidden.
		const pendingAmazonProduct: ProductDetail = {
			...mockProductWithCustomFields,
			customFields: [],
			urls: [
				{
					id: 'url-amz',
					url: 'https://www.amazon.com/dp/B0XYZ',
					currency: 'USD',
					failureCount: 0,
					status: 'pending',
					isOutOfStock: false
				}
			]
		};

		it('should hide the store-configuration gear for a built-in store URL even before its first scrape', async () => {
			auth.login(mockUser);
			renderPage(pendingAmazonProduct, [builtInAmazon]);

			await waitFor(() => {
				expect(screen.getAllByText('MacBook Pro').length).toBeGreaterThanOrEqual(1);
			});

			expect(screen.queryByTitle('Store configuration')).not.toBeInTheDocument();
		});

		it('should show the store-configuration gear for a non-built-in store URL', async () => {
			auth.login(mockUser);
			// apple.com is not in the (empty) store list → treated as non-built-in/editable.
			renderPage(mockProductWithCustomFields, []);

			await waitFor(() => {
				expect(screen.getAllByText('MacBook Pro').length).toBeGreaterThanOrEqual(1);
			});

			expect(screen.getByTitle('Store configuration')).toBeInTheDocument();
		});
	});

	describe('alerts whose currency no longer matches the product', () => {
		// A product re-anchored from USD to EUR (ProductPriceAggregator) leaves its USD alerts
		// uncomparable. They stop firing server-side; the UI has to say so rather than leaving the
		// user with an alert that looks armed.
		const productWithDormantAlert: ProductDetail = {
			...mockProductWithCustomFields,
			currency: 'EUR',
			alerts: [
				{
					id: 'a1',
					targetPrice: 1800,
					condition: 'below',
					active: true,
					currency: 'USD',
					hasCurrencyMismatch: true
				}
			]
		};

		it('should format the target in the alert currency when it differs from the product', async () => {
			// The regression this replaces: the target was formatted with the PRODUCT's currency, so
			// a target of 1800 dollars rendered as "€1,800.00" — a threshold the user never set.
			auth.login(mockUser);
			renderPage(productWithDormantAlert);

			await waitFor(() => {
				expect(screen.getByText(/Below: \$1,800\.00/)).toBeInTheDocument();
			});
			expect(screen.queryByText(/Below: €1,800\.00/)).not.toBeInTheDocument();
		});

		it('should explain that a mismatched alert is dormant', async () => {
			auth.login(mockUser);
			renderPage(productWithDormantAlert);

			await waitFor(() => {
				expect(screen.getByTestId('alert-currency-mismatch')).toBeInTheDocument();
			});
			expect(screen.getByTestId('alert-currency-mismatch')).toHaveTextContent(/Dormant/);
		});

		it('should offer an in-app recovery action naming the product currency', async () => {
			// Earlier copy told the user to delete and recreate the alert, which was the only
			// recovery that existed. It loses the alert's trigger history and is not something a
			// user would guess at, so the recovery is now an action on the alert itself. The
			// re-anchor that causes dormancy is usually a property of where the worker egresses
			// from and may never revert, so waiting is not a strategy. See issue #127.
			auth.login(mockUser);
			renderPage(productWithDormantAlert);

			await waitFor(() => {
				expect(screen.getByTestId('redenominate-alert')).toBeInTheDocument();
			});
			// Names the currency the new target will be set in, so the action is concrete.
			expect(screen.getByTestId('redenominate-alert')).toHaveTextContent(/EUR/);
		});

		it('should re-denominate a dormant alert and leave dormancy', async () => {
			auth.login(mockUser);
			vi.mocked(api.redenominateAlert).mockResolvedValue({
				id: 'a1',
				productId: 'p1',
				productName: 'MacBook Pro',
				targetPrice: 1700,
				condition: 'below',
				active: true,
				currency: 'EUR',
				productCurrency: 'EUR',
				hasCurrencyMismatch: false
			});
			renderPage(productWithDormantAlert);

			await waitFor(() => {
				expect(screen.getByTestId('redenominate-alert')).toBeInTheDocument();
			});
			await fireEvent.click(screen.getByTestId('redenominate-alert'));

			const input = await screen.findByLabelText(/new target price in EUR/i);
			await fireEvent.input(input, { target: { value: '1700' } });
			await fireEvent.click(screen.getByRole('button', { name: /set target/i }));

			// The row leaves dormancy and shows the new target in the product's currency.
			await waitFor(() => {
				expect(screen.queryByTestId('alert-currency-mismatch')).not.toBeInTheDocument();
			});
			expect(screen.getByText(/Below: €1,700\.00/)).toBeInTheDocument();
			// Sends the currency the user was actually shown, so a re-anchor mid-edit is caught.
			expect(api.redenominateAlert).toHaveBeenCalledWith('a1', 1700, 'EUR');
			expect(api.getProduct).not.toHaveBeenCalled();
		});

		it('should keep the alert dormant and show the error when re-denominating fails', async () => {
			auth.login(mockUser);
			vi.mocked(api.redenominateAlert).mockRejectedValue(
				new Error('This product is now priced in GBP, not EUR.')
			);
			renderPage(productWithDormantAlert);

			await waitFor(() => {
				expect(screen.getByTestId('redenominate-alert')).toBeInTheDocument();
			});
			await fireEvent.click(screen.getByTestId('redenominate-alert'));
			await fireEvent.click(screen.getByRole('button', { name: /set target/i }));

			await waitFor(() => {
				expect(screen.getByTestId('redenominate-error')).toHaveTextContent(/now priced in GBP/);
			});
			// The alert must not read as recovered when the server refused it.
			expect(screen.getByTestId('alert-currency-mismatch')).toBeInTheDocument();
		});

		it('should not mark a matching alert as dormant', async () => {
			auth.login(mockUser);
			renderPage({
				...mockProductWithCustomFields,
				alerts: [
					{
						id: 'a1',
						targetPrice: 1800,
						condition: 'below',
						active: true,
						currency: 'USD',
						hasCurrencyMismatch: false
					}
				]
			});

			await waitFor(() => {
				expect(screen.getByText(/Below: \$1,800\.00/)).toBeInTheDocument();
			});
			expect(screen.queryByTestId('alert-currency-mismatch')).not.toBeInTheDocument();
		});
	});

	describe('alert mutations use the mutation response, not a product refetch', () => {
		const productWithAlert: ProductDetail = {
			...mockProductWithCustomFields,
			alerts: [
				{
					id: 'a1',
					targetPrice: 1800,
					condition: 'below',
					active: true,
					currency: 'USD',
					hasCurrencyMismatch: false
				}
			]
		};

		it('should remove a deleted alert locally without refetching the product', async () => {
			auth.login(mockUser);
			vi.mocked(api.deleteAlert).mockResolvedValue(undefined as never);
			renderPage(productWithAlert);

			await waitFor(() => {
				expect(screen.getByText(/Below: \$1,800\.00/)).toBeInTheDocument();
			});

			await fireEvent.click(screen.getByLabelText('Delete alert'));

			await waitFor(() => {
				expect(screen.queryByText(/Below: \$1,800\.00/)).not.toBeInTheDocument();
			});
			expect(api.deleteAlert).toHaveBeenCalledWith('a1');
			expect(api.getProduct).not.toHaveBeenCalled();
		});

		it('should pause an alert from the response without refetching the product', async () => {
			auth.login(mockUser);
			vi.mocked(api.setAlertActive).mockResolvedValue({ active: false } as never);
			renderPage(productWithAlert);

			await fireEvent.click(await screen.findByLabelText('Pause alert'));

			await waitFor(() => {
				expect(screen.getByLabelText('Resume alert')).toBeInTheDocument();
			});
			expect(api.setAlertActive).toHaveBeenCalledWith('a1', false);
			expect(api.getProduct).not.toHaveBeenCalled();
		});

		it('should append a created alert locally without refetching the product', async () => {
			auth.login(mockUser);
			vi.mocked(api.createAlert).mockResolvedValue({
				id: 'a2',
				productId: 'p1',
				productName: 'MacBook Pro',
				targetPrice: 1500,
				condition: 'below',
				active: true,
				currency: 'USD',
				productCurrency: 'USD',
				hasCurrencyMismatch: false
			});
			renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				expect(screen.getByText('Create Alert')).toBeInTheDocument();
			});
			await fireEvent.click(screen.getByText('Create Alert'));

			const dialog = await screen.findByRole('dialog');
			await fireEvent.input(within(dialog).getByLabelText(/target price/i), {
				target: { value: '1500' }
			});
			await fireEvent.click(within(dialog).getByRole('button', { name: /create alert/i }));

			await waitFor(() => {
				expect(screen.getByText(/Below: \$1,500\.00/)).toBeInTheDocument();
			});
			expect(api.createAlert).toHaveBeenCalledWith('p1', 1500, 'below');
			expect(api.getProduct).not.toHaveBeenCalled();
		});

		it('should denominate a created alert from the response when it differs from the product', async () => {
			// The page used to substitute product.currency and a hardcoded hasCurrencyMismatch: false,
			// because CreateAlert.Response carried neither. Both are real fields now, so this asserts
			// the page reports what the server said rather than re-deriving it. The two agree in
			// production — they are forced apart here precisely because agreement hides the bug.
			auth.login(mockUser);
			vi.mocked(api.createAlert).mockResolvedValue({
				id: 'a2',
				productId: 'p1',
				productName: 'MacBook Pro',
				targetPrice: 1500,
				condition: 'below',
				active: true,
				currency: 'GBP',
				productCurrency: 'USD',
				hasCurrencyMismatch: true
			});
			renderPage(mockProductWithCustomFields);

			await waitFor(() => {
				expect(screen.getByText('Create Alert')).toBeInTheDocument();
			});
			await fireEvent.click(screen.getByText('Create Alert'));

			const dialog = await screen.findByRole('dialog');
			await fireEvent.input(within(dialog).getByLabelText(/target price/i), {
				target: { value: '1500' }
			});
			await fireEvent.click(within(dialog).getByRole('button', { name: /create alert/i }));

			await waitFor(() => {
				expect(screen.getByText(/Below: £1,500\.00/)).toBeInTheDocument();
			});
		});
	});
});
