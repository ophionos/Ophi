<script lang="ts">
	import type { Product } from '$lib/api/client';
	import PriceChangeChip from '$lib/components/products/PriceChangeChip.svelte';

	interface Props {
		product: Product;
	}

	let { product }: Props = $props();
</script>

<!-- A product with less than 7 days of history has no 7-day change: say so instead of "0%". -->
{#if product.change7d == null}
	<span class="text-xs text-gray-500 dark:text-gray-400" data-testid="change-new">New</span>
{:else}
	<PriceChangeChip change={product.change7d} />
{/if}
