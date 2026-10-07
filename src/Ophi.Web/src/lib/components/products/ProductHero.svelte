<script lang="ts">
	import { Clock, ImageOff } from 'lucide-svelte';
	import { fly } from 'svelte/transition';
	import type { ProductDetail } from '$lib/api/client';
	import { formatCheckInterval } from '$lib/format';
	import { getPriceStory } from '$lib/utils/priceStory';
	import TagBadge from '$lib/components/tags/TagBadge.svelte';

	interface Props {
		product: ProductDetail;
	}

	let { product }: Props = $props();

	const priceStory = $derived(
		getPriceStory({
			currentPrice: product.currentPrice,
			previousPrice: product.previousPrice,
			priceChange: product.priceChange,
			priceMin: product.statistics.min > 0 ? product.statistics.min : undefined,
			priceMax: product.statistics.max > 0 ? product.statistics.max : undefined
		})
	);

	const storyVariantClasses: Record<string, string> = {
		positive: 'bg-green-100 dark:bg-green-900/40 text-green-700 dark:text-green-400',
		negative: 'bg-red-100 dark:bg-red-900/40 text-red-700 dark:text-red-400',
		neutral: 'bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400',
		highlight: 'bg-brand-subtle dark:bg-brand-dark/40 text-brand dark:text-brand-muted'
	};
</script>

<div
	class="relative bg-surface-1 rounded-2xl shadow-xl border border-gray-200 dark:border-white/5 overflow-hidden ring-1 ring-black/5 mb-8"
	in:fly={{ y: 20, duration: 500, delay: 100 }}
>
	<!-- Ambient background glow behind image -->
	<div
		class="absolute -top-32 -left-32 w-64 h-64 bg-brand/20 dark:bg-brand/10 rounded-full blur-3xl pointer-events-none"
	></div>

	<div class="relative flex flex-col md:flex-row p-5 md:p-6 z-10">
		{#if product.imageUrl}
			<div
				class="w-full md:w-40 h-32 shrink-0 rounded-xl overflow-hidden shadow-lg border border-white/10 bg-white/50 dark:bg-gray-800/50 backdrop-blur-sm mb-6 md:mb-0"
			>
				<img
					src={product.imageUrl}
					alt={product.name}
					loading="lazy"
					class="w-full h-full object-contain p-2"
				/>
			</div>
		{:else}
			<div
				class="w-full md:w-40 h-12 md:h-32 shrink-0 bg-gray-100/50 dark:bg-gray-800/50 backdrop-blur-sm rounded-xl border border-white/10 flex items-center justify-center gap-2 text-sm text-gray-400 dark:text-gray-500 mb-4 md:mb-0"
			>
				<ImageOff size={16} aria-hidden="true" />
				No Image
			</div>
		{/if}

		<div class="md:ml-6 flex-1 flex flex-col justify-center min-w-0">
			<div class="flex items-center gap-3 mb-4">
				<h1
					class="text-2xl md:text-3xl font-bold text-gray-900 dark:text-white leading-tight max-w-2xl"
				>
					{product.name}
				</h1>
			</div>
			{#if priceStory}
				<div class="mb-3">
					<span
						class="text-xs font-medium px-2 py-1 rounded-full {storyVariantClasses[
							priceStory.variant
						]}"
						data-testid="price-story"
					>
						{priceStory.text}
					</span>
				</div>
			{/if}

			<p
				class="text-sm text-gray-400 dark:text-gray-500 flex flex-wrap items-center gap-x-2 gap-y-0.5"
			>
				<span class="inline-flex items-center gap-1 whitespace-nowrap">
					<Clock size={14} class="opacity-70" />
					{#if product.lastChecked}
						Updated {new Date(product.lastChecked).toLocaleString(undefined, {
							dateStyle: 'medium',
							timeStyle: 'short'
						})}
					{:else}
						Never checked
					{/if}
				</span>
				<span aria-hidden="true">&#183;</span>
				<span class="whitespace-nowrap">
					{#if product.checkIntervalMinutes}
						Checks every {formatCheckInterval(product.checkIntervalMinutes)}
					{:else}
						Checks every 1h (default)
					{/if}
				</span>
			</p>

			{#if product.tags && product.tags.length > 0}
				<div
					class="flex flex-wrap gap-2 mt-4 pt-4 border-t border-gray-100 dark:border-gray-800/50 w-full"
				>
					{#each product.tags as tag (tag.id)}
						<TagBadge name={tag.name} color={tag.color} />
					{/each}
				</div>
			{/if}
		</div>
	</div>
</div>
