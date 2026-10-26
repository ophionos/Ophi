<script lang="ts">
	import type { BasketHistorySeries } from '$lib/api/client';
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
		series: BasketHistorySeries[];
	}

	let { series }: Props = $props();
	let canvas = $state<HTMLCanvasElement>();
	let chartReady = $state(false);

	let uniqueCurrencies = $derived([...new Set(series.map((s) => s.currency))]);
	let hasMismatch = $derived(uniqueCurrencies.length > 1);

	$effect(() => {
		if (!canvas || series.length === 0) return;

		const ctx = canvas.getContext('2d');
		if (!ctx) return;

		let chart: { destroy(): void } | undefined;
		let cancelled = false;

		const themeColors = getChartThemeColors(theme.current === 'dark');
		const mismatch = uniqueCurrencies.length > 1;

		// Each series already ends at "now" (the API adds that point), and starts only when the
		// store had every item, so it is null before its first point.
		const aligned = alignTimeSeries(
			series.map((s) => s.points.map((p) => ({ date: p.date, price: p.total }))),
			{ holdToEnd: true }
		);

		const datasets = series.map((store, index) => {
			const colorSet = chartSeriesColor(index);
			const data = aligned[index];
			return {
				label: mismatch ? `${store.host} (${store.currency})` : store.host,
				data,
				borderColor: colorSet.border,
				backgroundColor: colorSet.fill,
				borderWidth: 2,
				fill: false,
				// Flat until the total changes, as in ComparisonChart.
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
				data: { datasets },
				options: {
					responsive: true,
					maintainAspectRatio: false,
					interaction: { mode: 'index', intersect: false },
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
									const store = series[context.datasetIndex];
									return `${store.host}: ${formatPrice(value, store.currency)}`;
								}
							}
						}
					},
					scales: {
						x: {
							...xAxis,
							grid: { display: false },
							ticks: { callback: tickLabel, color: themeColors.tickColor, font: { size: 11 } }
						},
						y: {
							beginAtZero: false,
							grid: { color: themeColors.gridColor },
							ticks: {
								color: themeColors.tickColor,
								font: { size: 11 },
								callback: (value) =>
									mismatch
										? Number(value).toFixed(2)
										: formatPrice(Number(value), series[0]?.currency || 'USD')
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
	{#if series.length === 0}
		<p class="text-sm text-gray-500 dark:text-gray-400">
			No store had a price for every item in this period.
		</p>
	{:else}
		{#if hasMismatch}
			<CurrencyMismatchBanner currencies={uniqueCurrencies} />
		{/if}
		<div class="relative h-80 w-full">
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
		<p class="mt-3 text-xs text-gray-500 dark:text-gray-400">
			Totals use the current shipping rate for every day. Periods when an item was out of stock
			do not show.
		</p>
	{/if}
</div>
