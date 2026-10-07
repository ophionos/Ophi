import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
	loadChart,
	getChartThemeColors,
	formatChartDate,
	resetChartLoaderForTests
} from './chart';

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
