import type { Chart as ChartType } from 'chart.js';

type ChartModule = typeof import('chart.js');
type ChartClass = typeof ChartType;

let chartModulePromise: Promise<ChartModule> | null = null;
let registered = false;

export async function loadChart(): Promise<ChartClass> {
	if (!chartModulePromise) {
		chartModulePromise = import('chart.js');
	}
	const mod = await chartModulePromise;
	if (!registered) {
		mod.Chart.register(
			mod.LineController,
			mod.LineElement,
			mod.PointElement,
			mod.LinearScale,
			mod.TimeScale,
			mod.CategoryScale,
			mod.Title,
			mod.Tooltip,
			mod.Legend,
			mod.Filler
		);
		registered = true;
	}
	return mod.Chart;
}

export interface ChartThemeColors {
	tooltipBg: string;
	tooltipTitle: string;
	tooltipBody: string;
	tooltipBorder: string;
	gridColor: string;
	tickColor: string;
	legendColor: string;
	pointBorder: string;
}

export function getChartThemeColors(isDark: boolean): ChartThemeColors {
	return {
		tooltipBg: isDark ? 'rgba(31, 41, 55, 0.95)' : 'rgba(255, 255, 255, 0.95)',
		tooltipTitle: isDark ? '#f9fafb' : '#1f2937',
		tooltipBody: isDark ? '#d1d5db' : '#4b5563',
		tooltipBorder: isDark ? '#374151' : '#e5e7eb',
		gridColor: isDark ? '#374151' : '#f3f4f6',
		tickColor: isDark ? '#9ca3af' : '#9ca3af',
		legendColor: isDark ? '#d1d5db' : '#4b5563',
		pointBorder: isDark ? '#1f2937' : '#fff'
	};
}

export function formatChartDate(date: string | Date, includeYear = false): string {
	const d = typeof date === 'string' ? new Date(date) : date;
	const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
	if (includeYear) {
		opts.year = 'numeric';
		opts.weekday = 'short';
	}
	return d.toLocaleDateString('en-US', opts);
}

export function resetChartLoaderForTests(): void {
	chartModulePromise = null;
	registered = false;
}
