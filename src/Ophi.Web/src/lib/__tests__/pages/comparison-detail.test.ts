import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ComparisonDetailPage from '../../../routes/comparisons/[id]/+page.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { api } from '$lib/api/client';
import { goto } from '$app/navigation';
import { page } from '$app/state';
import type { ComparisonGroupDetail, Product } from '$lib/api/client';
import type { PageData } from '../../../routes/comparisons/[id]/$types';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			getComparisonGroup: vi.fn(),
			getProducts: vi.fn(),
			addProductsToComparisonGroup: vi.fn(),
			removeProductFromComparisonGroup: vi.fn(),
			deleteComparisonGroup: vi.fn()
		}
	};
});

vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

vi.mock('chart.js', () => {
	function MockChart() {
		return { destroy: vi.fn(), update: vi.fn() };
	}
	return {
		Chart: Object.assign(MockChart, { register: vi.fn(), defaults: { font: {} } }),
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

const mockGroupDetail: ComparisonGroupDetail = {
	id: 'g1',
	name: 'Laptops',
	description: 'Compare laptop prices',
	products: [
		{
			id: 'p1',
			name: 'MacBook Pro',
			url: 'https://apple.com/macbook',
			currentPrice: 1999.99,
			currency: 'USD',
			isBestPrice: false,
			priceHistory: [
				{ date: '2025-01-01', price: 2099.99 },
				{ date: '2025-01-15', price: 1999.99 }
			],
			customFields: []
		},
		{
			id: 'p2',
			name: 'Dell XPS',
			url: 'https://dell.com/xps',
			currentPrice: 1499.99,
			currency: 'USD',
			isBestPrice: true,
			priceHistory: [
				{ date: '2025-01-01', price: 1599.99 },
				{ date: '2025-01-15', price: 1499.99 }
			],
			customFields: []
		}
	],
	bestPriceProductId: 'p2',
	bestPrice: 1499.99,
	updatedAt: '2025-01-15T10:00:00Z'
};

const mockGroupNoBestPrice: ComparisonGroupDetail = {
	id: 'g1',
	name: 'Empty Group',
	description: 'No products yet',
	products: [],
	updatedAt: '2025-01-15T10:00:00Z'
};

const mockUserProducts: Product[] = [
	{
		id: 'p1',
		name: 'MacBook Pro',
		url: 'https://apple.com/macbook',
		currentPrice: 1999.99,
		currency: 'USD',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: []
	},
	{
		id: 'p2',
		name: 'Dell XPS',
		url: 'https://dell.com/xps',
		currentPrice: 1499.99,
		currency: 'USD',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: []
	},
	{
		id: 'p3',
		name: 'ThinkPad X1',
		url: 'https://lenovo.com/thinkpad',
		currentPrice: 1299.99,
		currency: 'USD',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: []
	}
];

function setupPageParams(id: string) {
	page.params = { id };
}

function renderPage(
	group: ComparisonGroupDetail | null = mockGroupDetail,
	extraData: Record<string, unknown> = {}
) {
	return render(ComparisonDetailPage, {
		props: {
			data: {
				user: null,
				authChecked: true,
				isPublic: false,
				group,
				...extraData
			} as unknown as PageData
		}
	});
}

describe('Comparison Detail Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		auth.logout();
		setupPageParams('g1');
		auth.login(mockUser);
	});

	describe('rendering', () => {
		it('should render back link to comparisons', () => {
			renderPage();
			const backLink = screen.getByText('Back to Comparisons');
			expect(backLink).toBeInTheDocument();
			expect(backLink.closest('a')).toHaveAttribute('href', '/comparisons');
		});

		it('should render group name and description', () => {
			renderPage();
			expect(screen.getByText('Laptops')).toBeInTheDocument();
			expect(screen.getByText('Compare laptop prices')).toBeInTheDocument();
		});

		it('should render Add Product and Delete Group buttons', () => {
			renderPage();
			expect(screen.getByText('Add Product')).toBeInTheDocument();
			expect(screen.getByText('Delete Group')).toBeInTheDocument();
		});
	});

	describe('cheapest spotlight', () => {
		it('should name the cheapest product in neutral wording when bestPrice exists', () => {
			renderPage();
			const spotlight = screen.getByTestId('cheapest-spotlight');
			expect(spotlight).toHaveTextContent('Cheapest in this group');
			expect(spotlight).toHaveTextContent('Dell XPS');
			expect(spotlight).toHaveTextContent('$1,499.99');
		});

		it('should not frame the group as a contest when bestPrice exists', () => {
			// A group can be a wishlist: buying the cheapest item saves nothing on the others.
			const { container } = renderPage();
			expect(container.textContent).not.toMatch(/winner|you save|best price/i);
		});

		it('should not show the spotlight when no bestPrice', () => {
			renderPage(mockGroupNoBestPrice);
			expect(screen.getByText('Empty Group')).toBeInTheDocument();
			expect(screen.queryByTestId('cheapest-spotlight')).not.toBeInTheDocument();
		});
	});

	describe('price chart section', () => {
		it('should render Price Comparison heading', () => {
			renderPage();
			expect(screen.getByText('Price Comparison')).toBeInTheDocument();
		});

		it('should render day selector buttons (7d, 30d, 90d)', () => {
			renderPage();
			expect(screen.getByText('7d')).toBeInTheDocument();
			expect(screen.getByText('30d')).toBeInTheDocument();
			expect(screen.getByText('90d')).toBeInTheDocument();
		});
	});

	describe('products list', () => {
		it('should render products with prices', () => {
			renderPage();
			expect(screen.getByText('MacBook Pro')).toBeInTheDocument();
			expect(screen.getAllByText('Dell XPS').length).toBeGreaterThanOrEqual(1);
		});

		it('should show products count in heading', () => {
			renderPage();
			expect(screen.getByText('Products (2)')).toBeInTheDocument();
		});

		it('should show empty state when no products in group', () => {
			renderPage(mockGroupNoBestPrice);
			expect(screen.getByText('No products in this group')).toBeInTheDocument();
			expect(screen.getByText('Add products to compare their prices')).toBeInTheDocument();
			expect(
				screen.getByRole('button', { name: 'Add a product to this group' })
			).toBeInTheDocument();
		});

		it('should mark the cheapest row with a badge when bestPrice exists', () => {
			renderPage();
			const rows = screen.getAllByTestId('comparison-product-row');
			expect(rows[1]).toHaveTextContent('Cheapest');
			expect(rows[0]).not.toHaveTextContent('Cheapest');
		});

		it('should give every row the same thumbnail and name size when one row is cheapest', () => {
			renderPage();
			const [other, cheapest] = screen.getAllByTestId('comparison-product-row');
			const thumb = (row: HTMLElement) => row.firstElementChild!.getAttribute('class');
			const name = (row: HTMLElement) => row.querySelector('a')!.getAttribute('class');
			expect(thumb(cheapest)).toBe(thumb(other));
			expect(name(cheapest)).toBe(name(other));
		});
	});

	describe('day selector', () => {
		it('should call API with correct days param when 7d is clicked', async () => {
			vi.mocked(api.getComparisonGroup).mockResolvedValue(mockGroupDetail);
			renderPage();

			const sevenDayButton = screen.getByText('7d');
			await fireEvent.click(sevenDayButton);

			await waitFor(() => {
				expect(api.getComparisonGroup).toHaveBeenCalledWith('g1', 7);
			});
		});

		it('should call API with correct days param when 90d is clicked', async () => {
			vi.mocked(api.getComparisonGroup).mockResolvedValue(mockGroupDetail);
			renderPage();

			const ninetyDayButton = screen.getByText('90d');
			await fireEvent.click(ninetyDayButton);

			await waitFor(() => {
				expect(api.getComparisonGroup).toHaveBeenCalledWith('g1', 90);
			});
		});
	});

	describe('add products', () => {
		it('should open the add products modal on button click', async () => {
			vi.mocked(api.getProducts).mockResolvedValue({ items: mockUserProducts, total: 3, page: 1, pageSize: 10, atLowestCount: 0, priceDropCount: 0, withAlertsCount: 0, needsActionCount: 0, nearTargetCount: 0, productCount: 0, urlCount: 0, stores: [] });
			renderPage();

			await fireEvent.click(screen.getByText('Add Product'));

			// The modal title ("Add Products", plural) is distinct from the trigger ("Add Product").
			await waitFor(() => {
				expect(screen.getByText('Add Products')).toBeInTheDocument();
			});
		});

		it('should call the batch API and refresh data when products are added', async () => {
			vi.mocked(api.getComparisonGroup).mockResolvedValue(mockGroupDetail);
			vi.mocked(api.getProducts).mockResolvedValue({ items: mockUserProducts, total: 3, page: 1, pageSize: 10, atLowestCount: 0, priceDropCount: 0, withAlertsCount: 0, needsActionCount: 0, nearTargetCount: 0, productCount: 0, urlCount: 0, stores: [] });
			vi.mocked(api.addProductsToComparisonGroup).mockResolvedValue(undefined as never);
			renderPage();

			await fireEvent.click(screen.getByText('Add Product'));

			// p1/p2 are already in the group; ThinkPad (p3) is the selectable one.
			await waitFor(() => {
				expect(screen.getByText('ThinkPad X1')).toBeInTheDocument();
			});

			await fireEvent.click(screen.getByText('ThinkPad X1'));
			await fireEvent.click(screen.getByRole('button', { name: /^Add \(1\)$/ }));

			await waitFor(() => {
				expect(api.addProductsToComparisonGroup).toHaveBeenCalledWith('g1', ['p3']);
			});
		});
	});

	describe('remove product', () => {
		it('should show confirm modal when remove is clicked', async () => {
			renderPage();

			const removeButtons = screen.getAllByTitle('Remove from group');
			await fireEvent.click(removeButtons[0]);

			await waitFor(() => {
				expect(screen.getByText('Remove Product')).toBeInTheDocument();
				expect(screen.getByText(/Are you sure you want to remove 'MacBook Pro'/)).toBeInTheDocument();
			});
		});

		it('should call API and refresh data on confirm remove', async () => {
			vi.mocked(api.getComparisonGroup).mockResolvedValue(mockGroupDetail);
			vi.mocked(api.getProducts).mockResolvedValue({ items: mockUserProducts, total: 3, page: 1, pageSize: 24, atLowestCount: 0, priceDropCount: 0, withAlertsCount: 0, needsActionCount: 0, nearTargetCount: 0, productCount: 0, urlCount: 0, stores: [] });
			vi.mocked(api.removeProductFromComparisonGroup).mockResolvedValue(undefined as never);
			renderPage();

			const removeButtons = screen.getAllByTitle('Remove from group');
			await fireEvent.click(removeButtons[0]);

			await waitFor(() => {
				expect(screen.getByText('Remove Product')).toBeInTheDocument();
			});

			const confirmButton = screen.getByRole('button', { name: /^Remove$/i });
			await fireEvent.click(confirmButton);

			await waitFor(() => {
				expect(api.removeProductFromComparisonGroup).toHaveBeenCalledWith('g1', 'p1');
			});
		});
	});

	describe('product attributes table', () => {
		const mockGroupWithCustomFields: ComparisonGroupDetail = {
			id: 'g1',
			name: 'Laptops',
			description: 'Compare laptop prices',
			products: [
				{
					id: 'p1',
					name: 'MacBook Pro',
					url: 'https://apple.com/macbook',
					currentPrice: 1999.99,
					currency: 'USD',
					isBestPrice: false,
					priceHistory: [],
					customFields: [
						{ name: 'RAM', value: '16GB' },
						{ name: 'Storage', value: '512GB' }
					]
				},
				{
					id: 'p2',
					name: 'Dell XPS',
					url: 'https://dell.com/xps',
					currentPrice: 1499.99,
					currency: 'USD',
					isBestPrice: true,
					priceHistory: [],
					customFields: [
						{ name: 'RAM', value: '32GB' },
						{ name: 'Screen', value: '15 inch' }
					]
				}
			],
			bestPriceProductId: 'p2',
			bestPrice: 1499.99,
			updatedAt: '2025-01-15T10:00:00Z'
		};

		it('should display product attributes table', () => {
			const { container } = renderPage(mockGroupWithCustomFields);

			expect(container.querySelector('[data-testid="product-attributes-table"]')).toBeInTheDocument();
			expect(screen.getByText('Product Attributes')).toBeInTheDocument();

			expect(screen.getByText('RAM')).toBeInTheDocument();
			expect(screen.getByText('Storage')).toBeInTheDocument();
			expect(screen.getByText('Screen')).toBeInTheDocument();

			expect(screen.getByText('16GB')).toBeInTheDocument();
			expect(screen.getByText('32GB')).toBeInTheDocument();
			expect(screen.getByText('512GB')).toBeInTheDocument();
			expect(screen.getByText('15 inch')).toBeInTheDocument();
		});

		it('should show dash for missing values', () => {
			const { container } = renderPage(mockGroupWithCustomFields);

			const cells = container.querySelectorAll('[data-testid="product-attributes-table"] td');
			const cellTexts = Array.from(cells).map((c) => c.textContent?.trim());
			expect(cellTexts).toContain('-');
		});

		it('should use field order from first product', () => {
			const { container } = renderPage(mockGroupWithCustomFields);

			const rows = container.querySelectorAll('[data-testid="product-attributes-table"] tbody tr');
			const fieldNames = Array.from(rows).map(
				(row) => row.querySelector('td')?.textContent?.trim()
			);

			expect(fieldNames).toEqual(['RAM', 'Storage', 'Screen']);
		});

		it('should not show attributes table when no products have custom fields', () => {
			const { container } = renderPage();
			expect(container.querySelector('[data-testid="product-attributes-table"]')).not.toBeInTheDocument();
		});
	});

	describe('delete group', () => {
		it('should show confirm modal when Delete Group is clicked', async () => {
			renderPage();

			await fireEvent.click(screen.getByText('Delete Group'));

			await waitFor(() => {
				expect(screen.getByText('Delete Comparison Group')).toBeInTheDocument();
				expect(
					screen.getByText('Are you sure you want to delete this comparison group? This action cannot be undone.')
				).toBeInTheDocument();
			});
		});

		it('should call API and redirect to /comparisons on confirm', async () => {
			vi.mocked(api.deleteComparisonGroup).mockResolvedValue(undefined as never);
			renderPage();

			await fireEvent.click(screen.getByText('Delete Group'));

			await waitFor(() => {
				expect(screen.getByText('Delete Comparison Group')).toBeInTheDocument();
			});

			const confirmButton = screen.getByRole('button', { name: /^Delete$/i });
			await fireEvent.click(confirmButton);

			await waitFor(() => {
				expect(api.deleteComparisonGroup).toHaveBeenCalledWith('g1');
				expect(goto).toHaveBeenCalledWith('/comparisons');
			});
		});
	});

	describe('landed prices', () => {
		const product = (id: string, name: string, extra: Record<string, unknown>) => ({
			id,
			name,
			url: `https://${id}.example/x`,
			currentPrice: 18.99,
			currency: 'EUR',
			isBestPrice: false,
			priceHistory: [],
			customFields: [],
			...extra
		});

		// Scraped cheapest is "Cheap" (18.99), but its shipping makes "Free" the landed best.
		const landedGroup = {
			id: 'g1',
			name: 'Gadgets',
			updatedAt: '2025-01-15T10:00:00Z',
			bestPriceProductId: 'cheap',
			bestPrice: 18.99,
			bestLandedProductId: 'free',
			bestLandedPrice: 19.99,
			products: [
				product('cheap', 'Cheap', {
					isBestPrice: true,
					landedPrice: 21.99,
					shippingCost: 3,
					landedHost: 'cheap.example',
					isBestLandedPrice: false
				}),
				product('free', 'Free', {
					currentPrice: 19.99,
					landedPrice: 19.99,
					shippingCost: 0,
					landedHost: 'free.example',
					isBestLandedPrice: true
				}),
				product('norate', 'NoRate', {
					currentPrice: 25,
					landedPrice: 25,
					shippingCost: 0,
					landedHost: 'norate.example',
					isBestLandedPrice: false
				}),
				product('foreign', 'Foreign', {
					currentPrice: 5,
					landedPrice: null,
					shippingCost: null,
					landedHost: 'foreign.example',
					isBestLandedPrice: false
				})
			]
		} as unknown as ComparisonGroupDetail;

		const rates = [
			{ host: 'free.example', amount: 4, currency: 'EUR', freeAbove: 15 },
			{ host: 'cheap.example', amount: 3, currency: 'EUR', freeAbove: null }
		];

		// Matched on the product link's exact text: the "Cheapest" badge also contains "Cheap".
		const nameOf = (r: HTMLElement) => r.querySelector('a[href^="/products/"]')?.textContent?.trim();
		const row = (name: string) =>
			screen.getAllByTestId('comparison-product-row').find((r) => nameOf(r) === name)!;

		it('should rank by the store price and show no shipping text when the setting is off', () => {
			renderPage(landedGroup, { showLandedPrices: false, shippingRates: [] });
			expect(row('Cheap')).toHaveTextContent('Cheapest');
			expect(row('Free')).not.toHaveTextContent('Cheapest');
			expect(screen.getByTestId('cheapest-spotlight')).toHaveTextContent('Cheap');
			expect(screen.getByTestId('cheapest-spotlight')).not.toHaveTextContent('incl. shipping');
			expect(screen.getByTestId('product-price-cheap')).not.toHaveTextContent(
				'shipping'
			);
		});

		it('should mark the best landed product instead of the best store price when the setting is on', () => {
			renderPage(landedGroup, { showLandedPrices: true, shippingRates: rates });
			expect(row('Free')).toHaveTextContent('Cheapest');
			expect(row('Cheap')).not.toHaveTextContent('Cheapest');
			const spotlight = screen.getByTestId('cheapest-spotlight');
			expect(spotlight).toHaveTextContent('Free');
			expect(spotlight).toHaveTextContent('€19.99');
			expect(spotlight).toHaveTextContent('incl. shipping');
		});

		it('should show the landed price with its parts when shipping is above zero', () => {
			renderPage(landedGroup, { showLandedPrices: true, shippingRates: rates });
			expect(row('Cheap')).toHaveTextContent('€21.99');
			expect(row('Cheap')).toHaveTextContent('incl. shipping (€18.99 + €3.00 shipping)');
		});

		it('should take the parts from the landed URL when it is not the cheapest store price', () => {
			// The product's cheapest store price (17.00) is at a site whose shipping makes it
			// dearer; the landed price comes from another site's 18.99 + 3.00.
			const group = {
				...landedGroup,
				products: [
					product('cheap', 'Cheap', {
						currentPrice: 17,
						landedPrice: 21.99,
						shippingCost: 3,
						landedHost: 'cheap.example',
						isBestLandedPrice: true
					})
				]
			} as unknown as ComparisonGroupDetail;
			renderPage(group, { showLandedPrices: true, shippingRates: rates });
			expect(row('Cheap')).toHaveTextContent('incl. shipping (€18.99 + €3.00 shipping)');
		});

		it('should order the rows by landed price, unknown last, when the setting is on', () => {
			renderPage(landedGroup, { showLandedPrices: true, shippingRates: rates });
			const names = screen.getAllByTestId('comparison-product-row').map(nameOf);
			expect(names).toEqual(['Free', 'Cheap', 'NoRate', 'Foreign']);
		});

		it('should keep the server order when the setting is off', () => {
			renderPage(landedGroup, { showLandedPrices: false, shippingRates: [] });
			const names = screen.getAllByTestId('comparison-product-row').map(nameOf);
			expect(names).toEqual(['Cheap', 'Free', 'NoRate', 'Foreign']);
		});

		it('should say free shipping when the host has a rate and shipping is zero', () => {
			renderPage(landedGroup, { showLandedPrices: true, shippingRates: rates });
			expect(row('Free')).toHaveTextContent('incl. shipping (free shipping)');
		});

		it('should say no shipping cost set when the host has no rate and shipping is zero', () => {
			renderPage(landedGroup, { showLandedPrices: true, shippingRates: rates });
			expect(row('NoRate')).toHaveTextContent('incl. shipping (no shipping cost set)');
		});

		it('should show the store price with a currency note and never mark it best when landed is unknown', () => {
			renderPage(landedGroup, { showLandedPrices: true, shippingRates: rates });
			expect(row('Foreign')).toHaveTextContent('€5.00');
			expect(row('Foreign')).toHaveTextContent('shipping in another currency');
			expect(row('Foreign')).not.toHaveTextContent('Cheapest');
			expect(row('Foreign')).not.toHaveTextContent('incl. shipping');
		});
	});
});
