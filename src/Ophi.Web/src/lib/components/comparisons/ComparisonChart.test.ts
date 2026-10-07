import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import type { ComparisonProduct } from '$lib/api/client';

// Mock Chart.js to prevent actual chart creation
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

import ComparisonChart from './ComparisonChart.svelte';

describe('ComparisonChart', () => {
	const mockProducts: ComparisonProduct[] = [
		{
			id: 'prod-1',
			name: 'Product 1',
			url: 'https://example.com/1',
			currency: 'USD',
			currentPrice: 99.99,
			isBestPrice: true,
			priceHistory: [
				{ date: '2024-01-01', price: 100 },
				{ date: '2024-01-02', price: 95 },
				{ date: '2024-01-03', price: 99.99 }
			],
			customFields: []
		},
		{
			id: 'prod-2',
			name: 'Product 2',
			url: 'https://example.com/2',
			currency: 'USD',
			currentPrice: 109.99,
			isBestPrice: false,
			priceHistory: [
				{ date: '2024-01-01', price: 110 },
				{ date: '2024-01-02', price: 115 },
				{ date: '2024-01-03', price: 109.99 }
			],
			customFields: []
		}
	];

	const defaultProps = {
		products: mockProducts
	};

	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('empty states', () => {
		it('should show empty message when products array is empty', () => {
			render(ComparisonChart, { props: { products: [] } });
			expect(screen.getByText('No products to compare')).toBeInTheDocument();
		});

		it('should show no history message when all products have empty history', () => {
			const productsWithoutHistory: ComparisonProduct[] = [
				{ ...mockProducts[0], priceHistory: [] },
				{ ...mockProducts[1], priceHistory: [] }
			];
			render(ComparisonChart, { props: { products: productsWithoutHistory } });
			expect(screen.getByText('No price history available')).toBeInTheDocument();
		});

		it('should not render canvas when no products', () => {
			const { container } = render(ComparisonChart, { props: { products: [] } });
			const canvas = container.querySelector('canvas');
			expect(canvas).not.toBeInTheDocument();
		});

		it('should not render canvas when all products have empty history', () => {
			const productsWithoutHistory: ComparisonProduct[] = [
				{ ...mockProducts[0], priceHistory: [] },
				{ ...mockProducts[1], priceHistory: [] }
			];
			const { container } = render(ComparisonChart, {
				props: { products: productsWithoutHistory }
			});
			const canvas = container.querySelector('canvas');
			expect(canvas).not.toBeInTheDocument();
		});
	});

	describe('with data', () => {
		it('should render canvas element when products have data', () => {
			const { container } = render(ComparisonChart, { props: defaultProps });
			const canvas = container.querySelector('canvas');
			expect(canvas).toBeInTheDocument();
		});

		it('should not show empty messages when products have data', () => {
			render(ComparisonChart, { props: defaultProps });
			expect(screen.queryByText('No products to compare')).not.toBeInTheDocument();
			expect(screen.queryByText('No price history available')).not.toBeInTheDocument();
		});

		it('should render with single product', () => {
			const singleProduct = [mockProducts[0]];
			const { container } = render(ComparisonChart, { props: { products: singleProduct } });
			const canvas = container.querySelector('canvas');
			expect(canvas).toBeInTheDocument();
		});
	});

	describe('currency mismatch warning', () => {
		it('should show currency mismatch warning when products have different currencies', () => {
			const mixedProducts: ComparisonProduct[] = [
				{ ...mockProducts[0], currency: 'USD' },
				{ ...mockProducts[1], currency: 'GBP' }
			];
			render(ComparisonChart, { props: { products: mixedProducts } });
			expect(screen.getByTestId('currency-mismatch-warning')).toBeInTheDocument();
		});

		it('should not show warning when all products have same currency', () => {
			render(ComparisonChart, { props: defaultProps });
			expect(screen.queryByTestId('currency-mismatch-warning')).not.toBeInTheDocument();
		});
	});

	describe('container styling', () => {
		it('should have full width class', () => {
			const { container } = render(ComparisonChart, { props: defaultProps });
			const wrapper = container.firstElementChild;
			expect(wrapper?.classList.contains('w-full')).toBe(true);
		});

		it('should wrap the canvas in a fixed-height box', () => {
			// The banner now lives outside the fixed-height box (so it can't push the canvas to
			// overflow and overlap the section below); the h-80 box wraps just the canvas.
			const { container } = render(ComparisonChart, { props: defaultProps });
			const chartBox = container.querySelector('.h-80');
			expect(chartBox).not.toBeNull();
			expect(chartBox?.querySelector('canvas')).not.toBeNull();
		});
	});
});
