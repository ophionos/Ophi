import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
	loadChart,
	CHART_SERIES_COLORS,
	getChartThemeColors,
	formatChartDate,
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
