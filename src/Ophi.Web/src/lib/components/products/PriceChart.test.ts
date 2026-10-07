import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import type { PriceHistoryPoint } from '$lib/api/client';

// Captures the config handed to `new Chart(ctx, config)` so tests can assert on scale options.
// Typed as just the slice the tests read: the y-axis bounds (undefined = left to Chart.js).
interface CapturedChartConfig {
	options: { scales: { y: { min?: number; max?: number } } };
}
const chartConfigs: CapturedChartConfig[] = [];

// Mock Chart.js to prevent actual chart creation
vi.mock('chart.js', () => {
	function MockChart(_ctx: unknown, config: unknown) {
		chartConfigs.push(config as CapturedChartConfig);
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

import PriceChart from './PriceChart.svelte';

describe('PriceChart', () => {
	const mockHistory: PriceHistoryPoint[] = [
		{ date: '2024-01-01', price: 100 },
		{ date: '2024-01-02', price: 95 },
		{ date: '2024-01-03', price: 105 },
		{ date: '2024-01-04', price: 90 }
	];

	const defaultProps = {
		history: mockHistory,
		currency: 'USD'
	};

	beforeEach(() => {
		vi.clearAllMocks();
		chartConfigs.length = 0;
	});

	describe('y-axis scaling', () => {
		// Chart.js expands the axis to ±5% of the value when every plotted point is identical
		// (its `min === max` branch), so a stable €294.78 price rendered a €280–€310 axis. That
		// looks broken next to the real prices on the page, and its floor can land above another
		// store's actual price.
		async function configFor(props: Record<string, unknown>) {
			render(PriceChart, { props: { ...defaultProps, ...props } });
			await vi.waitFor(() => expect(chartConfigs.length).toBeGreaterThan(0));
			return chartConfigs[chartConfigs.length - 1];
		}

		it('should pin a tight band when every price is identical', async () => {
			const flat = [
				{ date: '2024-01-01', price: 294.78 },
				{ date: '2024-01-02', price: 294.78 }
			];

			const y = (await configFor({ history: flat })).options.scales.y;

			expect(y.min).toBeDefined();
			expect(y.max).toBeDefined();
			// The value stays centred, and the band is far tighter than Chart.js's ±5% (±14.74).
			expect((y.min! + y.max!) / 2).toBeCloseTo(294.78, 5);
			expect(y.max! - y.min!).toBeLessThan(294.78 * 0.05);
		});

		it('should leave scaling to Chart.js when prices actually vary', async () => {
			const y = (await configFor({ history: mockHistory })).options.scales.y;

			expect(y.min).toBeUndefined();
			expect(y.max).toBeUndefined();
		});

		it('should pin a tight band when multiple stores all sit at the same price', async () => {
			const urlHistories = [
				{
					productUrlId: 'a',
					url: 'https://amazon.es/p',
					currency: 'EUR',
					history: [
						{ date: '2024-01-01', price: 294.78 },
						{ date: '2024-01-02', price: 294.78 }
					]
				},
				{
					productUrlId: 'b',
					url: 'https://worten.pt/p',
					currency: 'EUR',
					history: [
						{ date: '2024-01-01', price: 294.78 },
						{ date: '2024-01-02', price: 294.78 }
					]
				}
			];

			const y = (await configFor({ currency: 'EUR', urlHistories })).options.scales.y;

			expect(y.max! - y.min!).toBeLessThan(294.78 * 0.05);
		});

		it('should leave scaling alone when two stores hold different prices', async () => {
			// The reported bug's shape once the backend carries a stable store forward: two flat
			// series at different levels. The axis must span both, not get pinned to one.
			const urlHistories = [
				{
					productUrlId: 'a',
					url: 'https://amazon.es/p',
					currency: 'EUR',
					history: [
						{ date: '2024-01-01', price: 294.78 },
						{ date: '2024-01-02', price: 294.78 }
					]
				},
				{
					productUrlId: 'b',
					url: 'https://worten.pt/p',
					currency: 'EUR',
					history: [
						{ date: '2024-01-01', price: 279.99 },
						{ date: '2024-01-02', price: 279.99 }
					]
				}
			];

			const y = (await configFor({ currency: 'EUR', urlHistories })).options.scales.y;

			expect(y.min).toBeUndefined();
			expect(y.max).toBeUndefined();
		});
	});

	describe('empty state', () => {
		it('should show empty message when history is empty', () => {
			render(PriceChart, { props: { ...defaultProps, history: [] } });
			expect(screen.getByText('No price history available')).toBeInTheDocument();
		});

		it('should not render canvas when history is empty', () => {
			const { container } = render(PriceChart, { props: { ...defaultProps, history: [] } });
			const canvas = container.querySelector('canvas');
			expect(canvas).not.toBeInTheDocument();
		});
	});

	describe('with data', () => {
		it('should render canvas element when history has data', () => {
			const { container } = render(PriceChart, { props: defaultProps });
			const canvas = container.querySelector('canvas');
			expect(canvas).toBeInTheDocument();
		});

		it('should not show empty message when history has data', () => {
			render(PriceChart, { props: defaultProps });
			expect(screen.queryByText('No price history available')).not.toBeInTheDocument();
		});

		it('should show not enough data message with single data point', () => {
			const singlePoint = [{ date: '2024-01-01', price: 100 }];
			const { getByText } = render(PriceChart, {
				props: { ...defaultProps, history: singlePoint }
			});
			expect(getByText('Not enough data yet')).toBeInTheDocument();
		});
	});

	describe('currency mismatch warning', () => {
		it('should show currency mismatch warning when urlHistories have different currencies', () => {
			render(PriceChart, {
				props: {
					...defaultProps,
					urlHistories: [
						{
							productUrlId: '1',
							url: 'https://amazon.com/p',
							currency: 'USD',
							history: [{ date: '2024-01-01', price: 100 }]
						},
						{
							productUrlId: '2',
							url: 'https://amazon.co.uk/p',
							currency: 'GBP',
							history: [{ date: '2024-01-01', price: 80 }]
						}
					]
				}
			});
			expect(screen.getByTestId('currency-mismatch-warning')).toBeInTheDocument();
		});

		it('should not show warning when urlHistories have same currency', () => {
			render(PriceChart, {
				props: {
					...defaultProps,
					urlHistories: [
						{
							productUrlId: '1',
							url: 'https://amazon.com/p1',
							currency: 'USD',
							history: [{ date: '2024-01-01', price: 100 }]
						},
						{
							productUrlId: '2',
							url: 'https://amazon.com/p2',
							currency: 'USD',
							history: [{ date: '2024-01-01', price: 90 }]
						}
					]
				}
			});
			expect(screen.queryByTestId('currency-mismatch-warning')).not.toBeInTheDocument();
		});

		it('should not show warning when no urlHistories', () => {
			render(PriceChart, { props: defaultProps });
			expect(screen.queryByTestId('currency-mismatch-warning')).not.toBeInTheDocument();
		});
	});

	describe('loading state', () => {
		it('should show loading indicator when loading is true', () => {
			render(PriceChart, { props: { ...defaultProps, loading: true } });
			expect(screen.getByTestId('chart-loading')).toBeInTheDocument();
			expect(screen.getByText('Loading price history...')).toBeInTheDocument();
		});

		it('should not render canvas when loading', () => {
			const { container } = render(PriceChart, { props: { ...defaultProps, loading: true } });
			expect(container.querySelector('canvas')).not.toBeInTheDocument();
		});

		it('should not show loading when loading is false', () => {
			render(PriceChart, { props: defaultProps });
			expect(screen.queryByTestId('chart-loading')).not.toBeInTheDocument();
		});
	});

	describe('accessibility', () => {
		it('should expose the chart as an image with an accessible name', () => {
			const { container } = render(PriceChart, { props: defaultProps });
			const figure = screen.getByRole('img', { name: 'Price history chart' });
			expect(figure).toContainElement(container.querySelector('canvas'));
		});

		it('should have aria-describedby linking to data summary', () => {
			render(PriceChart, { props: defaultProps });
			const figure = screen.getByRole('img', { name: 'Price history chart' });
			expect(figure).toHaveAttribute('aria-describedby', 'chart-data-summary');
			expect(document.getElementById('chart-data-summary')).toBeInTheDocument();
		});

		it('should include data point count in summary', () => {
			render(PriceChart, { props: defaultProps });
			const summary = document.getElementById('chart-data-summary');
			expect(summary?.textContent).toContain('4 data points');
		});

		it('should include price range in summary', () => {
			render(PriceChart, { props: defaultProps });
			const summary = document.getElementById('chart-data-summary');
			expect(summary?.textContent).toContain('$90.00');
			expect(summary?.textContent).toContain('$105.00');
		});

		it('should have sr-only class on data summary', () => {
			render(PriceChart, { props: defaultProps });
			const summary = document.getElementById('chart-data-summary');
			expect(summary?.classList.contains('sr-only')).toBe(true);
		});
	});

	describe('container styling', () => {
		it('should have full width class', () => {
			const { container } = render(PriceChart, { props: defaultProps });
			const wrapper = container.firstElementChild;
			expect(wrapper?.classList.contains('w-full')).toBe(true);
		});

		it('should have height class', () => {
			const { container } = render(PriceChart, { props: defaultProps });
			const wrapper = container.firstElementChild;
			expect(wrapper?.classList.contains('h-64')).toBe(true);
		});
	});
});
