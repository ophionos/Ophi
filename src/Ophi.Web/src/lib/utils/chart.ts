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

/** A point on a linear time axis: `x` is epoch milliseconds, `y` is null where a series has no price. */
export interface TimePoint {
	x: number;
	y: number | null;
}

/**
 * Puts price series on one shared time axis (#134). Each point keeps its real time, so a scrape at
 * 00:00:07 sits at the start of its day instead of one even slot after the day before. Every series
 * gets the same timestamps (an `index` tooltip lines them up) and holds its last price between its
 * own first and last point. Outside that span it is null: a store added today draws nothing before
 * today, and one that stopped draws nothing after. `holdToEnd` keeps the last price to the end of
 * the axis, for history that has no point at "now" (comparisons).
 */
export function alignTimeSeries(
	series: readonly { date: string; price: number }[][],
	{ holdToEnd = false } = {}
): TimePoint[][] {
	const xs = [...new Set(series.flatMap((s) => s.map((p) => Date.parse(p.date))))].sort((a, b) => a - b);
	return series.map((s) => {
		const prices = new Map(s.map((p) => [Date.parse(p.date), p.price]));
		const first = Math.min(...prices.keys());
		const last = holdToEnd ? Infinity : Math.max(...prices.keys());
		let held: number | null = null;
		return xs.map((x) => {
			held = prices.get(x) ?? held;
			return { x, y: x >= first && x <= last ? held : null };
		});
	});
}

/**
 * Tick positions for a time axis: local midnights inside [min, max], so each label names one day
 * once. On long ranges it steps several days at a time to stay within `maxTicks`.
 */
export function dayTicks(min: number, max: number, maxTicks = 7): number[] {
	const day = new Date(min);
	day.setHours(0, 0, 0, 0);
	if (day.getTime() < min) day.setDate(day.getDate() + 1);

	const spanDays = Math.max(0, Math.floor((max - day.getTime()) / 86_400_000));
	const step = Math.max(1, Math.ceil(spanDays / Math.max(1, maxTicks - 1)));
	const ticks: number[] = [];
	// setDate, not +86_400_000: a day is 23 or 25 hours across a DST change.
	for (; day.getTime() <= max; day.setDate(day.getDate() + step)) ticks.push(day.getTime());
	return ticks;
}

/**
 * The x-scale for price series on a time axis: linear in epoch milliseconds, bounded by the data,
 * with one tick per day (see `dayTicks`). There is no Chart.js date adapter, so the `time` scale
 * is not an option.
 */
export function timeAxis(series: readonly TimePoint[][]) {
	const xs = series.flatMap((s) => s.map((p) => p.x));
	const min = Math.min(...xs);
	// One instant (a comparison group whose prices are all from today) gives a zero-width axis,
	// because explicit bounds stop Chart.js from widening it. Show that whole day instead.
	const max = Math.max(...xs) > min ? Math.max(...xs) : min + 86_400_000;
	return {
		type: 'linear' as const,
		min,
		max,
		afterBuildTicks: (axis: { ticks: { value: number }[] }) => {
			axis.ticks = dayTicks(min, max).map((value) => ({ value }));
		},
		tickLabel: (value: number | string) => formatChartDate(new Date(Number(value)))
	};
}

export function resetChartLoaderForTests(): void {
	chartModulePromise = null;
	registered = false;
}
