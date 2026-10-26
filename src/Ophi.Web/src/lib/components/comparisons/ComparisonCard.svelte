<script lang="ts">
	import { resolve } from '$app/paths';
	import { Trash2, GitCompare } from 'lucide-svelte';
	import type { ComparisonGroup } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import Sparkline from '$lib/components/products/Sparkline.svelte';

	interface Props {
		group: ComparisonGroup;
		onDelete?: (group: ComparisonGroup) => void;
		/** Rank by landed price (store price + shipping). Off = the store price, as always. */
		showLandedPrices?: boolean;
	}

	let { group, onDelete, showLandedPrices = false }: Props = $props();
</script>

<!-- The name link stretches over the card (after:absolute), so a click anywhere opens the group;
     the delete button sits above it with `relative`. -->
<div
	data-testid="comparison-card"
	class="relative min-w-0 bg-surface-1 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 p-4 hover:shadow-md transition-shadow focus-within:ring-2 focus-within:ring-brand"
>
	<div class="flex items-start justify-between">
		<div class="flex-1 min-w-0">
			<div class="flex items-center gap-2">
				<GitCompare size={18} class="shrink-0 text-brand dark:text-brand-muted" />
				<h3 class="font-medium text-gray-900 dark:text-white truncate">
					<a
						href={resolve(`/comparisons/${group.id}`)}
						class="after:absolute after:inset-0 after:rounded-lg focus:outline-none hover:text-brand dark:hover:text-brand-muted"
					>
						{group.name}
					</a>
				</h3>
			</div>
			<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
				{group.productCount}
				{group.productCount === 1 ? 'product' : 'products'}
			</p>
			{#if showLandedPrices && group.bestLandedPrice != null && group.bestLandedProductName && group.currency}
				<div class="mt-3 flex items-center justify-between gap-3">
					<p
						class="min-w-0 truncate text-sm text-gray-600 dark:text-gray-300"
						data-testid="comparison-card-best"
					>
						Cheapest
						<span class="font-semibold text-gray-900 dark:text-white"
							>{formatPrice(group.bestLandedPrice, group.currency)}</span
						>
						incl. shipping · {group.bestLandedProductName}
					</p>
					<Sparkline data={group.sparkline ?? []} width={72} height={24} />
				</div>
			{:else if group.bestPrice != null && group.currency}
				<div class="mt-3 flex items-center justify-between gap-3">
					<p
						class="min-w-0 truncate text-sm text-gray-600 dark:text-gray-300"
						data-testid="comparison-card-best"
					>
						Cheapest
						<span class="font-semibold text-gray-900 dark:text-white"
							>{formatPrice(group.bestPrice, group.currency)}</span
						>
						· {group.bestPriceProductName}
					</p>
					<Sparkline data={group.sparkline ?? []} width={72} height={24} />
				</div>
			{/if}
		</div>

		{#if onDelete}
			<button
				onclick={() => onDelete?.(group)}
				class="relative flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-danger hover:bg-danger/10 rounded"
				title="Delete"
				aria-label="Delete {group.name}"
			>
				<Trash2 size={16} />
			</button>
		{/if}
	</div>
</div>
