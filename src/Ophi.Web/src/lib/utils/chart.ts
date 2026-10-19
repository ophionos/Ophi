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
		// Chart.js draws on a canvas, so it does not inherit the page font from CSS.
		mod.Chart.defaults.font.family = "'Signika Negative Variable', sans-serif";
		registered = true;
	}
	return mod.Chart;
}

/**
 * Categorical colours for chart series, in order. Brand teal first; no green or red, because those
 * mean "price dropped" and "price rose" everywhere else, and neighbours are far apart in hue.
 */
export const CHART_SERIES_COLORS: readonly { border: string; fill: string }[] = [
	{ border: 'rgb(13, 148, 136)', fill: 'rgba(13, 148, 136, 0.1)' }, // teal
	{ border: 'rgb(99, 102, 241)', fill: 'rgba(99, 102, 241, 0.1)' }, // indigo
	{ border: 'rgb(245, 158, 11)', fill: 'rgba(245, 158, 11, 0.1)' }, // amber
	{ border: 'rgb(236, 72, 153)', fill: 'rgba(236, 72, 153, 0.1)' }, // pink
	{ border: 'rgb(14, 165, 233)', fill: 'rgba(14, 165, 233, 0.1)' }, // sky
	{ border: 'rgb(139, 92, 246)', fill: 'rgba(139, 92, 246, 0.1)' } // violet
];

export function chartSeriesColor(index: number): { border: string; fill: string } {
	return CHART_SERIES_COLORS[index % CHART_SERIES_COLORS.length];
}

/** Above this many points, markers cover the line (a 1-year range has about 75). */
export const POINT_MARKER_LIMIT = 30;

/**
 * The `pointRadius` for one dataset: every point up to `limit`, otherwise only the lowest price
 * (its latest occurrence) and the latest point. Hover still shows every point.
 */
export function pointRadii(
	data: readonly (number | null)[],
	radius: number,
	limit = POINT_MARKER_LIMIT
): number | number[] {
	if (data.length <= limit) return radius;

	let lowest = -1;
	let latest = -1;
	data.forEach((value, i) => {
		if (value == null) return;
		latest = i;
		if (lowest === -1 || value <= (data[lowest] as number)) lowest = i;
	});
	return data.map((_, i) => (i === lowest || i === latest ? radius : 0));
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
