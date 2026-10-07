<script lang="ts">
	import type { PriceHistoryPoint } from '$lib/api/client';
	import { theme } from '$lib/stores/theme.svelte';
	import CurrencyMismatchBanner from '$lib/components/shared/CurrencyMismatchBanner.svelte';
	import { loadChart, getChartThemeColors, formatChartDate } from '$lib/utils/chart';
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

	const multiLineColors = [
		'rgb(13, 148, 136)',
		'rgb(16, 185, 129)',
		'rgb(245, 158, 11)',
		'rgb(239, 68, 68)'
	];

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
	function flatBand(datasets: { data: (number | null)[] }[]): { min: number; max: number } | object {
		const values = datasets.flatMap((d) => d.data).filter((v): v is number => typeof v === 'number');
		if (values.length === 0) return {};

		const lowest = Math.min(...values);
		if (lowest !== Math.max(...values)) return {};

		const pad = flatBandPad(lowest);
		return { min: lowest - pad, max: lowest + pad };
	}
	let canvas = $state<HTMLCanvasElement>();

	$effect(() => {
		if (!canvas || history.length <= 1) return;

		const ctx = canvas.getContext('2d');
		if (!ctx) return;

		let chart: { destroy(): void } | undefined;
		let cancelled = false;

		const isDark = theme.current === 'dark';
		const colors = getChartThemeColors(isDark);

		const isMultiLine = urlHistories && urlHistories.length >= 2;
		const mismatch = isMultiLine && uniqueCurrencies.length > 1;

		let datasets: any[];
		let labels: string[];

		let allDatesArray: string[] = [];
		if (isMultiLine) {
			const allDates = new Set<string>();
			for (const uh of urlHistories) {
				for (const h of uh.history) {
					allDates.add(h.date);
				}
			}
			allDatesArray = [...allDates].sort();
		} else {
			allDatesArray = history.map(h => h.date).sort();
		}

		if (isMultiLine) {
			const sortedDates = allDatesArray;
			labels = sortedDates.map(d => formatChartDate(d, false));

			datasets = urlHistories.map((uh, i) => {
				const color = multiLineColors[i % multiLineColors.length];
				const dateMap = new Map(uh.history.map((h) => [h.date, h.price]));
				const data = allDatesArray.map((d) => dateMap.get(d) ?? null);
				const domainLabel = getDomain(uh.url);
				const label = mismatch ? `${domainLabel} (${uh.currency})` : domainLabel;

				return {
					label,
					data,
					borderColor: color,
					backgroundColor: color,
					borderWidth: 2,
					fill: false,
					tension: 0.4,
					pointRadius: 3,
					pointHoverRadius: 6,
					pointBackgroundColor: color,
					pointBorderColor: colors.pointBorder,
					pointBorderWidth: 2,
					spanGaps: true
				};
			});
		} else {
			labels = history.map((h) => formatChartDate(h.date, false));
			const prices = history.map((h) => h.price);

			const gradient = ctx.createLinearGradient(0, 0, 0, 300);
			gradient.addColorStop(0, 'rgba(13, 148, 136, 0.3)');
			gradient.addColorStop(1, 'rgba(13, 148, 136, 0)');

			datasets = [
				{
					label: `Price (${currency})`,
					data: prices,
					borderColor: 'rgb(13, 148, 136)',
					backgroundColor: gradient,
					borderWidth: 3,
					fill: true,
					tension: 0.4,
					pointRadius: 4,
					pointHoverRadius: 6,
					pointBackgroundColor: 'rgb(13, 148, 136)',
					pointBorderColor: colors.pointBorder,
					pointBorderWidth: 2
				}
			];
		}

		loadChart().then((Chart) => {
			if (cancelled || !canvas) return;
			chart = new Chart(ctx, {
				type: 'line',
				data: {
					labels,
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
									size: 12,
									family: 'system-ui, -apple-system, sans-serif'
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
								title: (tooltipItems) => {
									const idx = tooltipItems[0].dataIndex;
									if (isMultiLine) {
										return formatChartDate(allDatesArray[idx], true);
									}
									return formatChartDate(history[idx].date, true);
								},
								label: (context) => {
									const value = context.parsed.y;
									if (value === null) return '';
									if (mismatch && urlHistories) {
										const uh = urlHistories[context.datasetIndex];
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
							grid: {
								display: false
							},
							border: { display: false },
							ticks: {
								maxTicksLimit: 7,
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
<div class="w-full h-64">
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
		<div id="chart-data-summary" class="sr-only">
			Price history chart showing {history.length} data points
			from {history[0]?.date ?? 'unknown'} to {history[history.length - 1]?.date ?? 'unknown'}.
			Prices range from {formatPrice(Math.min(...history.map(h => h.price)), currency)}
			to {formatPrice(Math.max(...history.map(h => h.price)), currency)}.
		</div>
	{/if}
</div>
