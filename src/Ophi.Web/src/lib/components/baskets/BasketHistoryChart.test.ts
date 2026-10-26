import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/svelte';
import type { BasketHistorySeries } from '$lib/api/client';

// Captures the config handed to `new Chart(ctx, config)`: only the slice the tests read.
interface CapturedChartConfig {
	data: { datasets: { label: string; stepped?: string; data: { x: number; y: number | null }[] }[] };
	options: { scales: { x: { type?: string } } };
}
const chartConfigs: CapturedChartConfig[] = [];

vi.mock('chart.js', () => {
	function MockChart(_ctx: unknown, config: unknown) {
		chartConfigs.push(config as CapturedChartConfig);
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

import BasketHistoryChart from './BasketHistoryChart.svelte';

const shopA: BasketHistorySeries = {
	host: 'shop-a.es',
	currency: 'EUR',
	points: [
		{ date: '2026-10-01T00:00:00Z', total: 30 },
		{ date: '2026-10-03T00:00:00Z', total: 28 },
		{ date: '2026-10-10T00:00:00Z', total: 28 }
	]
};
const shopB: BasketHistorySeries = {
	host: 'shop-b.es',
	currency: 'EUR',
	points: [
		{ date: '2026-10-05T00:00:00Z', total: 31 },
		{ date: '2026-10-10T00:00:00Z', total: 31 }
	]
};

describe('BasketHistoryChart', () => {
	beforeEach(() => {
		chartConfigs.length = 0;
	});

	it('should draw one stepped line per store on a time axis when there are series', async () => {
		render(BasketHistoryChart, { props: { series: [shopA, shopB] } });

		await waitFor(() => expect(chartConfigs).toHaveLength(1));
		const [config] = chartConfigs;
		expect(config.data.datasets.map((d) => d.label)).toEqual(['shop-a.es', 'shop-b.es']);
		expect(config.data.datasets.every((d) => d.stepped === 'before')).toBe(true);
		expect(config.options.scales.x.type).toBe('linear');
	});

	it('should leave a store empty before its series starts when the stores start at different times', async () => {
		render(BasketHistoryChart, { props: { series: [shopA, shopB] } });

		await waitFor(() => expect(chartConfigs).toHaveLength(1));
		const shopBData = chartConfigs[0].data.datasets[1].data;
		expect(shopBData[0].y).toBeNull();
		expect(shopBData.at(-1)?.y).toBe(31);
	});

	it('should state that shipping uses the current rate and that stock gaps do not show when drawn', () => {
		render(BasketHistoryChart, { props: { series: [shopA] } });

		expect(screen.getByText(/current shipping rate/)).toBeInTheDocument();
		expect(screen.getByText(/out of stock/)).toBeInTheDocument();
	});

	it('should name the currency in each label when the stores use different currencies', async () => {
		render(BasketHistoryChart, {
			props: { series: [shopA, { ...shopB, currency: 'USD' }] }
		});

		await waitFor(() => expect(chartConfigs).toHaveLength(1));
		expect(chartConfigs[0].data.datasets.map((d) => d.label)).toEqual([
			'shop-a.es (EUR)',
			'shop-b.es (USD)'
		]);
	});

	it('should explain that no store had every item when there are no series', () => {
		render(BasketHistoryChart, { props: { series: [] } });

		expect(screen.getByText(/No store had a price for every item/)).toBeInTheDocument();
		expect(chartConfigs).toHaveLength(0);
	});
});
