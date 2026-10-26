<script lang="ts">
	import type { PriceHistoryPoint } from '$lib/api/client';
	import { theme } from '$lib/stores/theme.svelte';
	import CurrencyMismatchBanner from '$lib/components/shared/CurrencyMismatchBanner.svelte';
	import {
		loadChart,
		getChartThemeColors,
		formatChartDate,
		chartSeriesColor,
		pointRadii,
		alignTimeSeries,
		timeAxis,
		type TimePoint
	} from '$lib/utils/chart';
	import { formatPrice } from '$lib/format';

	import { ChartNoAxesColumn, LoaderCircle } from 'lucide-svelte';

	interface UrlHistory {
		productUrlId: string;
		url: string;
		currency: string;
		history: { date: string; price: number }[];
	}

	interface Props {
		history: PriceHistoryPoint[];
		currency: string;
		urlHistories?: UrlHistory[];
		loading?: boolean;
	}

	let { history, currency, urlHistories, loading = false }: Props = $props();

	let uniqueCurrencies = $derived(
		urlHistories ? [...new Set(urlHistories.map((uh) => uh.currency))] : []
	);
	let hasMismatch = $derived(uniqueCurrencies.length > 1);

	function getDomain(url: string): string {
		try {
			return new URL(url).hostname;
		} catch {
			return url;
		}
	}

	// Half-height of the band drawn around a perfectly flat price series. 1% reads as flat without
	// Chart.js's ±5% zoom-out; the absolute floor keeps sub-euro prices from collapsing to a line
	// with no band at all.
	function flatBandPad(value: number): number {
		return Math.max(Math.abs(value) * 0.01, 0.5);
	}

	// Chart.js expands the y-axis to ±5% of the value when every plotted point is identical (its
	// `min === max` branch), so a stable €294.78 price produced a €280–€310 axis — a flat line
	// adrift in a 30-euro band whose floor can sit above a *different* store's real price, making
	// the cheaper store look like it fell off the bottom of the chart. Pin a tight symmetric band
	// for that degenerate case only; any genuine spread is left to Chart.js.
	function flatBand(datasets: { data: TimePoint[] }[]): { min: number; max: number } | object {
		const values = datasets
			.flatMap((d) => d.data.map((p) => p.y))
			.filter((v): v is number => typeof v === 'number');
		if (values.length === 0) return {};

		const lowest = Math.min(...values);
		if (lowest !== Math.max(...values)) return {};

		const pad = flatBandPad(lowest);
		return { min: lowest - pad, max: lowest + pad };
	}
	let canvas = $state<HTMLCanvasElement>();
	// Chart.js loads lazily; until the first chart is built the canvas would sit blank. It stays
	// true across redraws (the module is cached by then), so a range change does not flash it.
	let chartReady = $state(false);

	$effect(() => {
		if (!canvas || history.length <= 1) return;

		const ctx = canvas.getContext('2d');
		if (!ctx) return;

		let chart: { destroy(): void } | undefined;
		let cancelled = false;

		const isDark = theme.current === 'dark';
		const colors = getChartThemeColors(isDark);

		// A store with no price in the window (a paused one) has nothing to draw: no legend entry.
		const series = urlHistories?.filter((uh) => uh.history.length > 0) ?? [];
		const isMultiLine = series.length >= 2;
		const mismatch = isMultiLine && uniqueCurrencies.length > 1;

		let datasets: any[];

		// Points sit at their real time on a linear axis (#134): a category axis spaced a scrape at
		// 00:00:07 one full slot after the day before and repeated the day label for it. The backend
		// sends change-only history (held price at the window start, each change, the held price at
		// now), so the line steps instead of curving between changes. Chart.js 'before' runs flat to the
		// next point and then steps; 'after' would draw each change at the previous point's time.
		if (isMultiLine) {
			const aligned = alignTimeSeries(series.map((uh) => uh.history));
			datasets = series.map((uh, i) => {
				const color = chartSeriesColor(i).border;
				const data = aligned[i];
				const domainLabel = getDomain(uh.url);
				const label = mismatch ? `${domainLabel} (${uh.currency})` : domainLabel;

				return {
					label,
					data,
					borderColor: color,
					backgroundColor: color,
					borderWidth: 2,
					fill: false,
					stepped: 'before' as const,
					tension: 0,
					pointRadius: pointRadii(data.map((p) => p.y), 3),
					pointHoverRadius: 6,
					pointBackgroundColor: color,
					pointBorderColor: colors.pointBorder,
					pointBorderWidth: 2,
					spanGaps: false
				};
			});
		} else {
			const [data] = alignTimeSeries([history]);

			const gradient = ctx.createLinearGradient(0, 0, 0, 300);
			gradient.addColorStop(0, 'rgba(13, 148, 136, 0.3)');
			gradient.addColorStop(1, 'rgba(13, 148, 136, 0)');

			datasets = [
				{
					label: `Price (${currency})`,
					data,
					borderColor: 'rgb(13, 148, 136)',
					backgroundColor: gradient,
					borderWidth: 3,
					fill: true,
					stepped: 'before' as const,
					tension: 0,
					pointRadius: pointRadii(data.map((p) => p.y), 4),
					pointHoverRadius: 6,
					pointBackgroundColor: 'rgb(13, 148, 136)',
					pointBorderColor: colors.pointBorder,
					pointBorderWidth: 2
				}
			];
		}
		const { tickLabel, ...xAxis } = timeAxis(datasets.map((d) => d.data));

		loadChart().then((Chart) => {
			if (cancelled || !canvas) return;
			chartReady = true;
			chart = new Chart(ctx, {
				type: 'line',
				data: {
					datasets
				},
				options: {
					responsive: true,
					maintainAspectRatio: false,
					layout: {
						padding: {
							left: 10,
							right: 15,
							bottom: 5
						}
					},
					interaction: {
						mode: 'index',
						intersect: false
					},
					plugins: {
						legend: {
							display: isMultiLine,
							labels: {
								color: colors.tickColor,
								usePointStyle: true,
								pointStyle: 'circle',
								padding: 16,
								font: {
									size: 12
								}
							}
						},
						tooltip: {
							backgroundColor: colors.tooltipBg,
							titleColor: colors.tooltipTitle,
							bodyColor: colors.tooltipBody,
							borderColor: colors.tooltipBorder,
							borderWidth: 1,
							padding: 12,
							cornerRadius: 12,
							titleFont: { size: 13, weight: 'bold' },
							bodyFont: { size: 13 },
							displayColors: isMultiLine,
							callbacks: {
								title: (tooltipItems) => formatChartDate(new Date(Number(tooltipItems[0].parsed.x)), true),
								label: (context) => {
									const value = context.parsed.y;
									if (value === null) return '';
									if (mismatch) {
										const uh = series[context.datasetIndex];
										const prefix = `${context.dataset.label}: `;
										return `${prefix}${formatPrice(value, uh.currency)}`;
									}
									const prefix = isMultiLine ? `${context.dataset.label}: ` : '';
									return `${prefix}${formatPrice(value, currency)}`;
								}
							}
						}
					},
					scales: {
						x: {
							...xAxis,
							grid: {
								display: false
							},
							border: { display: false },
							ticks: {
								callback: tickLabel,
								color: colors.tickColor,
								font: {
									size: 11
								}
							}
						},
						y: {
							beginAtZero: false,
							...flatBand(datasets),
							border: { display: false },
							grid: {
								display: false
							},
							ticks: {
								color: colors.tickColor,
								font: {
									size: 11
								},
								callback: (value) => {
									if (mismatch) {
										return Number(value).toFixed(2);
									}
									return formatPrice(Number(value), currency);
								}
							}
						}
					}
				}
			});
		});

		return () => {
			cancelled = true;
			chart?.destroy();
		};
	});
</script>

{#if hasMismatch}
	<CurrencyMismatchBanner currencies={uniqueCurrencies} />
{/if}
<div class="relative w-full h-64">
	{#if loading}
		<div class="h-full flex flex-col items-center justify-center gap-2 text-gray-400 dark:text-gray-500" data-testid="chart-loading">
			<LoaderCircle size={32} class="animate-spin opacity-40" />
			<p class="text-sm">Loading price history...</p>
		</div>
	{:else if history.length === 0}
		<div class="h-full flex flex-col items-center justify-center gap-2 text-gray-400 dark:text-gray-500">
			<ChartNoAxesColumn size={32} class="opacity-40" />
			<p class="text-sm">No price history available</p>
		</div>
	{:else if history.length === 1}
		<div class="h-full flex flex-col items-center justify-center gap-2 text-gray-400 dark:text-gray-500">
			<ChartNoAxesColumn size={32} class="opacity-40" />
			<p class="text-sm font-medium text-gray-500 dark:text-gray-400">Not enough data yet</p>
			<p class="text-xs">Check back after the next price update</p>
		</div>
	{:else}
		<div
			class="h-full w-full"
			role="img"
			aria-label="Price history chart"
			aria-describedby="chart-data-summary"
		>
			<canvas bind:this={canvas}></canvas>
		</div>
		<!-- A sibling of the role="img" box: an image's children are hidden from screen readers. -->
		{#if !chartReady}
			<div
				class="absolute inset-0 flex items-center justify-center text-sm text-gray-500 dark:text-gray-400"
				role="status"
			>
				Loading chart…
			</div>
		{/if}
		<div id="chart-data-summary" class="sr-only">
			Price history chart showing {history.length} data points
			from {history[0]?.date ?? 'unknown'} to {history[history.length - 1]?.date ?? 'unknown'}.
			Prices range from {formatPrice(Math.min(...history.map(h => h.price)), currency)}
			to {formatPrice(Math.max(...history.map(h => h.price)), currency)}.
		</div>
	{/if}
</div>
