<script lang="ts">
	import type { ComparisonProduct } from '$lib/api/client';
	import { theme } from '$lib/stores/theme.svelte';
	import CurrencyMismatchBanner from '$lib/components/shared/CurrencyMismatchBanner.svelte';
	import {
		loadChart,
		getChartThemeColors,
		chartSeriesColor,
		pointRadii,
		alignTimeSeries,
		timeAxis,
		formatChartDate
	} from '$lib/utils/chart';
	import { formatPrice } from '$lib/format';

	interface Props {
		products: ComparisonProduct[];
	}

	let { products }: Props = $props();
	let canvas = $state<HTMLCanvasElement>();
	// Chart.js loads lazily; until the first chart is built the canvas would sit blank. It stays
	// true across redraws (the module is cached by then), so a range change does not flash it.
	let chartReady = $state(false);

	let uniqueCurrencies = $derived([...new Set(products.map((p) => p.currency))]);
	let hasMismatch = $derived(uniqueCurrencies.length > 1);

	$effect(() => {
		if (!canvas || products.length === 0) return;

		const ctx = canvas.getContext('2d');
		if (!ctx) return;

		let chart: { destroy(): void } | undefined;
		let cancelled = false;

		const isDark = theme.current === 'dark';
		const themeColors = getChartThemeColors(isDark);
		const mismatch = uniqueCurrencies.length > 1;

		// Points sit at their real time on a linear axis (#134). The history is change-only with no
		// point at "now", so a price holds from its first known point to the end of the axis. Before
		// that point it stays null: we do not know the price back then.
		const aligned = alignTimeSeries(
			products.map((p) => p.priceHistory),
			{ holdToEnd: true }
		);

		const datasets = products.map((product, index) => {
			const colorSet = chartSeriesColor(index);
			const data = aligned[index];
			const truncatedName =
				product.name.length > 30 ? product.name.substring(0, 30) + '...' : product.name;
			const label = mismatch ? `${truncatedName} (${product.currency})` : truncatedName;

			return {
				label,
				data,
				borderColor: colorSet.border,
				backgroundColor: colorSet.fill,
				borderWidth: 2,
				fill: false,
				// Stepped: price stays flat until it actually changes (matches the carry-forward data),
				// instead of diagonally interpolating between sparse points. Chart.js 'before' runs
				// flat to the next point, then steps; 'after' steps at the previous point, which drew
				// every change at the time of the scrape before it.
				stepped: 'before' as const,
				tension: 0,
				pointRadius: pointRadii(data.map((p) => p.y), 3),
				pointHoverRadius: 5,
				pointBackgroundColor: colorSet.border,
				pointBorderColor: themeColors.pointBorder,
				pointBorderWidth: 1,
				spanGaps: false
			};
		});
		const { tickLabel, ...xAxis } = timeAxis(aligned);

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
					interaction: {
						mode: 'index',
						intersect: false
					},
					plugins: {
						legend: {
							display: true,
							position: 'bottom',
							labels: {
								color: themeColors.legendColor,
								padding: 16,
								usePointStyle: true,
								boxWidth: 8
							}
						},
						tooltip: {
							backgroundColor: themeColors.tooltipBg,
							titleColor: themeColors.tooltipTitle,
							bodyColor: themeColors.tooltipBody,
							borderColor: themeColors.tooltipBorder,
							borderWidth: 1,
							padding: 12,
							callbacks: {
								title: (tooltipItems) => formatChartDate(new Date(Number(tooltipItems[0].parsed.x)), true),
								label: (context) => {
									const value = context.parsed.y;
									if (value === null) return '';
									const product = products[context.datasetIndex];
									return formatPrice(value, product.currency);
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
							ticks: {
								callback: tickLabel,
								color: themeColors.tickColor,
								font: {
									size: 11
								}
							}
						},
						y: {
							beginAtZero: false,
							grid: {
								color: themeColors.gridColor
							},
							ticks: {
								color: themeColors.tickColor,
								font: {
									size: 11
								},
								callback: (value) => {
									if (mismatch) {
										return Number(value).toFixed(2);
									}
									const currency = products[0]?.currency || 'USD';
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

<div class="w-full">
	{#if products.length === 0}
		<div class="h-80 flex items-center justify-center text-gray-500 dark:text-gray-400">
			No products to compare
		</div>
	{:else if products.every((p) => p.priceHistory.length === 0)}
		<div class="h-80 flex items-center justify-center text-gray-500 dark:text-gray-400">
			No price history available
		</div>
	{:else}
		<!-- Banner sits OUTSIDE the fixed-height canvas box: keeping it inside made the canvas
		     (responsive, maintainAspectRatio:false) overflow h-80, pushing the bottom legend onto
		     the section below. -->
		{#if hasMismatch}
			<CurrencyMismatchBanner currencies={uniqueCurrencies} />
		{/if}
		<div class="relative w-full h-80">
			<canvas bind:this={canvas}></canvas>
			{#if !chartReady}
				<div
					class="absolute inset-0 flex items-center justify-center text-sm text-gray-500 dark:text-gray-400"
					role="status"
				>
					Loading chart…
				</div>
			{/if}
		</div>
	{/if}
</div>
