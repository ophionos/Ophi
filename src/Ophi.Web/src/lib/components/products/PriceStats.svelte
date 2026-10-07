<script lang="ts">
	import { onDestroy } from 'svelte';
	import { fly } from 'svelte/transition';
	import type { ProductDetail } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import { createAnimatedPrice } from '$lib/utils/animatedPrice.svelte';
	import CurrencyMismatchBanner from '$lib/components/shared/CurrencyMismatchBanner.svelte';
	import StatCard from '$lib/components/shared/StatCard.svelte';
	import ConvertedPrice from '$lib/components/shared/ConvertedPrice.svelte';

	interface Props {
		product: ProductDetail;
		/** Id of the cheapest priced URL, highlighted in the per-store breakdowns. */
		bestPriceUrlId: string | null;
	}

	let { product, bestPriceUrlId }: Props = $props();

	const animMin = createAnimatedPrice(0);
	const animMax = createAnimatedPrice(0);
	const animAvg = createAnimatedPrice(0);
	const animCurrent = createAnimatedPrice(0);

	$effect(() => {
		animMin.target = product.statistics.min;
		animMax.target = product.statistics.max;
		animAvg.target = product.statistics.average;
		animCurrent.target = product.statistics.current ?? 0;
	});

	onDestroy(() => {
		animMin.destroy();
		animMax.destroy();
		animAvg.destroy();
		animCurrent.destroy();
	});

	const pricedUrls = $derived(product.urls?.filter((u) => u.currentPrice != null) ?? []);

	// Four identical non-zero stats means one data point — a stat row would just repeat it.
	const hasEnoughHistory = $derived.by(() => {
		const { min, max, average } = product.statistics;
		const current = product.statistics.current ?? 0;
		return !(min === max && max === average && average === current && min > 0);
	});

	const uniqueCurrencies = $derived(
		product.urls ? [...new Set(product.urls.map((u) => u.currency))] : []
	);

	function getDomain(url: string): string {
		try {
			return new URL(url).hostname;
		} catch {
			return url;
		}
	}
</script>

{#if product.hasCurrencyMismatch}
	<div class="mb-6" in:fly={{ y: 20, duration: 500, delay: 200 }}>
		<CurrencyMismatchBanner currencies={uniqueCurrencies} />
		<div class="grid grid-cols-2 md:grid-cols-4 gap-4">
			{#each pricedUrls as url (url.id)}
				<div
					class="bg-surface-1 p-4 rounded-xl shadow-sm border border-gray-200 dark:border-gray-700/50 ring-1 ring-black/5 dark:ring-white/5 transition-all duration-300 hover:-translate-y-1 hover:shadow-lg"
				>
					<p
						class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide font-semibold truncate"
						title={url.url}
					>
						{getDomain(url.url)}
					</p>
					<p
						class="text-xl font-bold mt-1 tabular-nums {url.id === bestPriceUrlId
							? 'text-green-600 dark:text-green-400'
							: 'text-gray-900 dark:text-white'}"
					>
						{url.currentPrice != null ? formatPrice(url.currentPrice, url.currency) : ''}
					</p>
					<ConvertedPrice amount={url.currentPrice} currency={url.currency} />
				</div>
			{/each}
		</div>
	</div>
{:else if hasEnoughHistory}
	<div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
		<div in:fly={{ y: 20, duration: 500, delay: 200 }}>
			<StatCard
				label="Lowest"
				valueClass="text-green-600 dark:text-green-400"
				value={product.statistics.min > 0 ? formatPrice(animMin.current, product.currency) : '-'}
			/>
		</div>
		<div in:fly={{ y: 20, duration: 500, delay: 250 }}>
			<StatCard
				label="Highest"
				valueClass="text-red-600 dark:text-red-400"
				value={product.statistics.max > 0 ? formatPrice(animMax.current, product.currency) : '-'}
			/>
		</div>
		<div in:fly={{ y: 20, duration: 500, delay: 300 }}>
			<StatCard
				label="Average"
				value={product.statistics.average > 0
					? formatPrice(animAvg.current, product.currency)
					: '-'}
			/>
		</div>
		<div in:fly={{ y: 20, duration: 500, delay: 350 }}>
			<StatCard
				label="Current"
				valueClass="text-brand dark:text-brand-muted"
				value={product.statistics.current
					? formatPrice(animCurrent.current, product.currency)
					: '-'}
			/>
			<ConvertedPrice class="block mt-1 px-1" amount={product.statistics.current} currency={product.currency} />
		</div>
	</div>
{:else if product.currentPrice}
	<div
		class="bg-surface-1 p-6 rounded-xl shadow-sm border border-gray-200 dark:border-gray-700/50 ring-1 ring-black/5 dark:ring-white/5 mb-6 text-center transition-all duration-300 hover:-translate-y-1 hover:shadow-lg"
		data-testid="single-price-display"
		in:fly={{ y: 20, duration: 500, delay: 200 }}
	>
		<p class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">
			Current Price
		</p>
		<p class="text-3xl font-bold text-gray-900 dark:text-white tabular-nums">
			{formatPrice(product.currentPrice, product.currency)}
		</p>
		<ConvertedPrice amount={product.currentPrice} currency={product.currency} />
		{#if pricedUrls.length > 1}
			<div class="mt-4 grid grid-cols-2 sm:grid-cols-3 gap-3 text-left">
				{#each pricedUrls as url (url.id)}
					<div class="rounded-lg bg-surface-2 px-3 py-2">
						<p class="text-xs text-gray-500 dark:text-gray-400 truncate" title={url.url}>
							{getDomain(url.url)}
						</p>
						<p
							class="text-sm font-bold tabular-nums {url.id === bestPriceUrlId
								? 'text-green-600 dark:text-green-400'
								: 'text-gray-900 dark:text-white'}"
						>
							{url.currency}
							{url.currentPrice?.toFixed(2)}
						</p>
					</div>
				{/each}
			</div>
		{:else}
			<p class="text-sm text-gray-400 dark:text-gray-500 mt-2">
				Not enough history for statistics
			</p>
		{/if}
	</div>
{/if}
