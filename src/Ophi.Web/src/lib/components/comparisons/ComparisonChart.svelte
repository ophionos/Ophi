<script lang="ts">
	import type { ComparisonProduct } from '$lib/api/client';
	import { theme } from '$lib/stores/theme.svelte';
	import CurrencyMismatchBanner from '$lib/components/shared/CurrencyMismatchBanner.svelte';
	import { loadChart, getChartThemeColors } from '$lib/utils/chart';
	import { formatPrice } from '$lib/format';

	interface Props {
		products: ComparisonProduct[];
	}

	let { products }: Props = $props();
	let canvas = $state<HTMLCanvasElement>();

	let uniqueCurrencies = $derived([...new Set(products.map((p) => p.currency))]);
	let hasMismatch = $derived(uniqueCurrencies.length > 1);

	const colors = [
		{ border: 'rgb(13, 148, 136)', bg: 'rgba(13, 148, 136, 0.1)' },
		{ border: 'rgb(16, 185, 129)', bg: 'rgba(16, 185, 129, 0.1)' },
		{ border: 'rgb(249, 115, 22)', bg: 'rgba(249, 115, 22, 0.1)' },
		{ border: 'rgb(139, 92, 246)', bg: 'rgba(139, 92, 246, 0.1)' },
		{ border: 'rgb(236, 72, 153)', bg: 'rgba(236, 72, 153, 0.1)' },
		{ border: 'rgb(245, 158, 11)', bg: 'rgba(245, 158, 11, 0.1)' },
		{ border: 'rgb(6, 182, 212)', bg: 'rgba(6, 182, 212, 0.1)' },
		{ border: 'rgb(239, 68, 68)', bg: 'rgba(239, 68, 68, 0.1)' }
	];

	function getAllDates(): string[] {
		const dateSet = new Set<string>();
		products.forEach((product) => {
			product.priceHistory.forEach((h) => dateSet.add(h.date));
		});
		return Array.from(dateSet).sort();
	}

	$effect(() => {
		if (!canvas || products.length === 0) return;

		const ctx = canvas.getContext('2d');
		if (!ctx) return;

		let chart: { destroy(): void } | undefined;
		let cancelled = false;

		const isDark = theme.current === 'dark';
		const themeColors = getChartThemeColors(isDark);
		const mismatch = uniqueCurrencies.length > 1;

		const allDates = getAllDates();
		const labels = allDates.map((date) => {
			const d = new Date(date);
			return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
		});

		const datasets = products.map((product, index) => {
			const colorSet = colors[index % colors.length];

			const priceMap = new Map<string, number>();
			product.priceHistory.forEach((h) => {
				priceMap.set(h.date, h.price);
			});

			// Carry-forward: a price holds at its last known value until it changes, so a product with
			// only one or two points still reads as a line instead of a lone floating dot. Dates before
			// a product's first known price stay null — we genuinely don't know the price back then.
			let lastKnown: number | null = null;
			const data = allDates.map((date) => {
				const price = priceMap.get(date);
				if (price !== undefined) lastKnown = price;
				return lastKnown;
			});
			const truncatedName =
				product.name.length > 30 ? product.name.substring(0, 30) + '...' : product.name;
			const label = mismatch ? `${truncatedName} (${product.currency})` : truncatedName;

			return {
				label,
				data,
				borderColor: colorSet.border,
				backgroundColor: colorSet.bg,
				borderWidth: 2,
				fill: false,
				// Stepped: price stays flat until it actually changes (matches the carry-forward data),
				// instead of diagonally interpolating between sparse points.
				stepped: 'after' as const,
				tension: 0,
				pointRadius: 3,
				pointHoverRadius: 5,
				pointBackgroundColor: colorSet.border,
				pointBorderColor: themeColors.pointBorder,
				pointBorderWidth: 1,
				spanGaps: true
			};
		});

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
								title: (tooltipItems) => {
									const idx = tooltipItems[0].dataIndex;
									const date = new Date(allDates[idx]);
									return date.toLocaleDateString('en-US', {
										weekday: 'short',
										month: 'short',
										day: 'numeric',
										year: 'numeric'
									});
								},
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
							grid: {
								display: false
							},
							ticks: {
								maxTicksLimit: 7,
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
		</div>
	{/if}
</div>
