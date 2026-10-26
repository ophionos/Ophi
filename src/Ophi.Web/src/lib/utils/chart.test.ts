import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
	loadChart,
	CHART_SERIES_COLORS,
	getChartThemeColors,
	formatChartDate,
	pointRadii,
	alignTimeSeries,
	dayTicks,
	timeAxis,
	resetChartLoaderForTests
} from './chart';

vi.mock('chart.js', () => {
	function MockChart() {
		return { destroy: vi.fn(), update: vi.fn() };
	}
	return {
		Chart: Object.assign(MockChart, { register: vi.fn(), defaults: { font: {} as { family?: string } } }),
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

describe('chart helpers', () => {
	describe('loadChart', () => {
		beforeEach(() => {
			resetChartLoaderForTests();
		});

		it('should return the Chart constructor', async () => {
			const Chart = await loadChart();
			expect(Chart).toBeDefined();
			expect(typeof Chart).toBe('function');
		});

		it('should register controllers exactly once across calls', async () => {
			const chartjs = await import('chart.js');
			const registerSpy = chartjs.Chart.register as ReturnType<typeof vi.fn>;
			registerSpy.mockClear();

			await loadChart();
			await loadChart();
			await loadChart();

			expect(registerSpy).toHaveBeenCalledTimes(1);
		});
	});

	describe('font', () => {
		beforeEach(() => {
			resetChartLoaderForTests();
		});

		it('should set the app font as the chart default font', async () => {
			const Chart = await loadChart();
			expect((Chart as unknown as { defaults: { font: { family?: string } } }).defaults.font.family).toContain(
				'Signika Negative Variable'
			);
		});
	});

	describe('CHART_SERIES_COLORS', () => {
		function hue(rgb: string): number {
			const [r, g, b] = (rgb.match(/\d+/g) ?? []).slice(0, 3).map((n) => Number(n) / 255);
			const max = Math.max(r, g, b);
			const d = max - Math.min(r, g, b);
			if (d === 0) return 0;
			const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
			return (h * 60 + 360) % 360;
		}

		it('should not repeat a colour', () => {
			const borders = CHART_SERIES_COLORS.map((c) => c.border);
			expect(new Set(borders).size).toBe(borders.length);
		});

		// Green and red mean "price dropped" and "price rose" everywhere else in the app.
		it('should not use the green or red hues of the price-change colours', () => {
			for (const { border } of CHART_SERIES_COLORS) {
				const h = hue(border);
				expect(h >= 90 && h <= 165, `${border} is green (hue ${h.toFixed(0)})`).toBe(false);
				expect(h < 15 || h > 345, `${border} is red (hue ${h.toFixed(0)})`).toBe(false);
			}
		});

		it('should keep neighbouring series at least 40 degrees of hue apart', () => {
			for (let i = 1; i < CHART_SERIES_COLORS.length; i++) {
				const a = hue(CHART_SERIES_COLORS[i - 1].border);
				const b = hue(CHART_SERIES_COLORS[i].border);
				const diff = Math.min(Math.abs(a - b), 360 - Math.abs(a - b));
				expect(diff).toBeGreaterThanOrEqual(40);
			}
		});
	});

	describe('getChartThemeColors', () => {
		it('should return dark palette when isDark', () => {
			const c = getChartThemeColors(true);
			expect(c.tooltipBg).toBe('rgba(31, 41, 55, 0.95)');
			expect(c.tooltipTitle).toBe('#f9fafb');
			expect(c.pointBorder).toBe('#1f2937');
		});

		it('should return light palette when not isDark', () => {
			const c = getChartThemeColors(false);
			expect(c.tooltipBg).toBe('rgba(255, 255, 255, 0.95)');
			expect(c.tooltipTitle).toBe('#1f2937');
			expect(c.pointBorder).toBe('#fff');
		});
	});

	describe('formatChartDate', () => {
		it('should format short date', () => {
			const out = formatChartDate('2024-03-15');
			expect(out).toMatch(/Mar/);
			expect(out).toMatch(/1[45]/); // tolerant of TZ shifts
		});

		it('should include year and weekday when requested', () => {
			const out = formatChartDate('2024-03-15', true);
			expect(out).toMatch(/2024/);
		});
	});
});

describe('pointRadii', () => {
	it('should give every point a marker up to the threshold', () => {
		expect(pointRadii([3, 1, 2], 4, 3)).toBe(4);
	});

	it('should keep only the lowest and the latest marker above the threshold', () => {
		expect(pointRadii([5, 2, 4, 3], 4, 3)).toEqual([0, 4, 0, 4]);
	});

	it('should mark the latest occurrence of a repeated lowest price', () => {
		expect(pointRadii([2, 5, 2, 4], 4, 3)).toEqual([0, 0, 4, 4]);
	});

	it('should skip gaps when finding the latest point', () => {
		expect(pointRadii([5, 2, 4, null], 4, 3)).toEqual([0, 4, 4, 0]);
	});

	it('should give no markers when every point is a gap', () => {
		expect(pointRadii([null, null, null, null], 4, 3)).toEqual([0, 0, 0, 0]);
	});
});

describe('alignTimeSeries', () => {
	const t = (iso: string) => Date.parse(iso);

	it('should place points at their real time, not at evenly spaced slots', () => {
		const [series] = alignTimeSeries([
			[
				{ date: '2024-01-01T00:00:00Z', price: 10 },
				{ date: '2024-01-07T00:00:00Z', price: 10 }
			]
		]);
		expect(series.map((p) => p.x)).toEqual([t('2024-01-01T00:00:00Z'), t('2024-01-07T00:00:00Z')]);
	});

	it('should give every series the same timestamps so an index tooltip lines up', () => {
		const [a, b] = alignTimeSeries([
			[
				{ date: '2024-01-01T00:00:00Z', price: 18.83 },
				{ date: '2024-01-08T12:00:00Z', price: 18.83 }
			],
			[
				{ date: '2024-01-08T00:00:07Z', price: 16.19 },
				{ date: '2024-01-08T12:00:00Z', price: 16.19 }
			]
		]);
		expect(a.map((p) => p.x)).toEqual(b.map((p) => p.x));
		expect(a).toHaveLength(3);
	});

	it('should leave a series empty before its first point and after its last point', () => {
		// #134: a URL first scraped today must not draw a line back to the window start.
		const [, added, ended] = alignTimeSeries([
			[
				{ date: '2024-01-01T00:00:00Z', price: 1 },
				{ date: '2024-01-08T12:00:00Z', price: 1 }
			],
			[
				{ date: '2024-01-08T00:00:07Z', price: 16.19 },
				{ date: '2024-01-08T12:00:00Z', price: 16.19 }
			],
			[
				{ date: '2024-01-01T00:00:00Z', price: 5 },
				{ date: '2024-01-03T00:00:00Z', price: 4 }
			]
		]);
		expect(added.map((p) => p.y)).toEqual([null, null, 16.19, 16.19]);
		expect(ended.map((p) => p.y)).toEqual([5, 4, null, null]);
	});

	it("should hold the last price between each series' own points", () => {
		const [, b] = alignTimeSeries([
			[
				{ date: '2024-01-01T00:00:00Z', price: 1 },
				{ date: '2024-01-02T00:00:00Z', price: 1 },
				{ date: '2024-01-03T00:00:00Z', price: 1 }
			],
			[
				{ date: '2024-01-01T00:00:00Z', price: 9 },
				{ date: '2024-01-03T00:00:00Z', price: 8 }
			]
		]);
		expect(b.map((p) => p.y)).toEqual([9, 9, 8]);
	});
});

describe('alignTimeSeries with holdToEnd', () => {
	it('should hold the last price to the end of the axis', () => {
		const [, b] = alignTimeSeries(
			[
				[
					{ date: '2024-01-01T00:00:00Z', price: 1 },
					{ date: '2024-01-02T00:00:00Z', price: 1 },
					{ date: '2024-01-03T00:00:00Z', price: 1 }
				],
				[{ date: '2024-01-02T00:00:00Z', price: 9 }]
			],
			{ holdToEnd: true }
		);
		expect(b.map((p) => p.y)).toEqual([null, 9, 9]);
	});
});

describe('dayTicks', () => {
	const midnight = (y: number, m: number, d: number) => new Date(y, m, d).getTime();

	it('should put one tick on each local midnight inside a short range', () => {
		const ticks = dayTicks(midnight(2024, 0, 1), midnight(2024, 0, 4) + 3_600_000);
		expect(ticks).toEqual([1, 2, 3, 4].map((d) => midnight(2024, 0, d)));
	});

	it('should never repeat a day', () => {
		// #134: two points on the same day gave two "Oct 26" labels.
		const ticks = dayTicks(midnight(2024, 0, 1), midnight(2024, 0, 8) + 43_200_000);
		const labels = ticks.map((v) => new Date(v).toDateString());
		expect(new Set(labels).size).toBe(labels.length);
	});

	it('should space the ticks evenly and keep them within the limit on a long range', () => {
		const ticks = dayTicks(midnight(2024, 0, 1), midnight(2024, 11, 31), 7);
		expect(ticks.length).toBeLessThanOrEqual(7);
		expect(ticks.length).toBeGreaterThan(1);
		expect(ticks[0]).toBe(midnight(2024, 0, 1));
	});

	it('should skip the start when it is not a midnight', () => {
		const ticks = dayTicks(midnight(2024, 0, 1) + 1, midnight(2024, 0, 3));
		expect(ticks).toEqual([midnight(2024, 0, 2), midnight(2024, 0, 3)]);
	});
});

describe('timeAxis', () => {
	it('should widen a range of one instant to a whole day so the axis has width', () => {
		// A comparison group created today: every product has one point, all on the same date.
		const at = new Date(2024, 0, 1).getTime();
		const axis = timeAxis([[{ x: at, y: 1 }], [{ x: at, y: 2 }]]);

		expect(axis.max).toBeGreaterThan(axis.min);
		const tickAxis = { ticks: [] as { value: number }[] };
		axis.afterBuildTicks(tickAxis);
		expect(tickAxis.ticks.map((t) => t.value)).toContain(at);
	});
});
