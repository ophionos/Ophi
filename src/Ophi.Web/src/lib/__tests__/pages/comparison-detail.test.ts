import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ComparisonDetailPage from '../../../routes/comparisons/[id]/+page.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { comparisons } from '$lib/stores/comparisons.svelte';
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
		Chart: Object.assign(MockChart, { register: vi.fn() }),
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

function renderPage(group: ComparisonGroupDetail | null = mockGroupDetail) {
	return render(ComparisonDetailPage, {
		props: {
			data: {
				user: null,
				authChecked: true,
				isPublic: false,
				group
			} as unknown as PageData
		}
	});
}

describe('Comparison Detail Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		auth.logout();
		comparisons.clear();
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

	describe('winner spotlight', () => {
		it('should show winner spotlight when bestPrice exists', () => {
			renderPage();
			expect(screen.getByText('Winner')).toBeInTheDocument();
			expect(screen.getByText('Best Price')).toBeInTheDocument();
			const dellTexts = screen.getAllByText(/Dell XPS/);
			expect(dellTexts.length).toBeGreaterThanOrEqual(1);
		});

		it('should show savings vs next cheapest', () => {
			renderPage();
			// Savings = 1999.99 - 1499.99 = 500.00
			expect(screen.getByText('You save')).toBeInTheDocument();
			expect(screen.getByText(/500\.00/)).toBeInTheDocument();
		});

		it('should not show winner spotlight when no bestPrice', () => {
			renderPage(mockGroupNoBestPrice);
			expect(screen.getByText('Empty Group')).toBeInTheDocument();
			expect(screen.queryByText('Winner')).not.toBeInTheDocument();
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

		it('should show Best Price badge on best price product', () => {
			renderPage();
			expect(screen.getByText('Best Price')).toBeInTheDocument();
			expect(screen.getByText('Winner')).toBeInTheDocument();
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
			vi.mocked(api.getProducts).mockResolvedValue({ items: mockUserProducts, total: 3, page: 1, pageSize: 10, atLowestCount: 0, priceDropCount: 0, withAlertsCount: 0 });
			renderPage();

			await fireEvent.click(screen.getByText('Add Product'));

			// The modal title ("Add Products", plural) is distinct from the trigger ("Add Product").
			await waitFor(() => {
				expect(screen.getByText('Add Products')).toBeInTheDocument();
			});
		});

		it('should call the batch API and refresh data when products are added', async () => {
			vi.mocked(api.getComparisonGroup).mockResolvedValue(mockGroupDetail);
			vi.mocked(api.getProducts).mockResolvedValue({ items: mockUserProducts, total: 3, page: 1, pageSize: 10, atLowestCount: 0, priceDropCount: 0, withAlertsCount: 0 });
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
			vi.mocked(api.getProducts).mockResolvedValue({ items: mockUserProducts, total: 3, page: 1, pageSize: 24, atLowestCount: 0, priceDropCount: 0, withAlertsCount: 0 });
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
});
