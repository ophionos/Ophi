<script lang="ts">
	import { LoaderCircle, Star } from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import type { Product, ProductStatus } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import ActionMenu from '$lib/components/shared/ActionMenu.svelte';
	import PriceChangeChip from '$lib/components/products/PriceChangeChip.svelte';
	import Sparkline from '$lib/components/products/Sparkline.svelte';
	import { productMenuItems } from '$lib/components/products/productMenu';
	import { attentionLabel, productInitial, storeLabel } from '$lib/utils/watchlist';

	interface Props {
		product: Product;
		onDelete?: (id: string) => void;
		onViewHistory?: (product: Product) => void;
		onStatusChange?: (id: string, status: ProductStatus) => void;
		onFavouriteToggle?: (id: string, isFavourite: boolean) => void;
		/** Bulk-selection mode: show a leading checkbox. */
		selectable?: boolean;
		selected?: boolean;
		onSelectToggle?: (id: string) => void;
	}

	let { product, onDelete, onViewHistory, onStatusChange, onFavouriteToggle, selectable = false, selected = false, onSelectToggle }: Props = $props();

	const isPending = $derived(product.status === 'pending');
	const menuItems = $derived(productMenuItems(product, { onViewHistory, onStatusChange, onDelete }));
</script>

<!-- The phone form of a table row: one line of identity, one of price. A healthy product shows its
     store under the name; a product with a problem shows the problem there instead. -->
<div class="flex items-center gap-2 rounded-lg border border-gray-200 bg-surface-1 py-2 pl-3 pr-1 dark:border-gray-700" data-testid="mobile-product-card">
	{#if selectable}
		<label class="-ml-2 flex min-h-11 min-w-11 shrink-0 cursor-pointer items-center justify-center">
			<input
				type="checkbox"
				checked={selected}
				onchange={() => onSelectToggle?.(product.id)}
				aria-label="Select {product.name}"
				class="h-4 w-4 cursor-pointer"
			/>
		</label>
	{/if}

	{#if isPending}
		<div class="flex h-10 w-10 shrink-0 items-center justify-center rounded bg-surface-2">
			<LoaderCircle size={16} class="animate-spin text-gray-400" />
		</div>
	{:else if product.imageUrl}
		<img src={product.imageUrl} alt="" loading="lazy" class="h-10 w-10 shrink-0 rounded object-cover" />
	{:else}
		<div class="flex h-10 w-10 shrink-0 items-center justify-center rounded bg-surface-2 font-semibold text-gray-500 dark:text-gray-400" aria-hidden="true" data-testid="product-initial">
			{productInitial(product.name)}
		</div>
	{/if}

	<div class="min-w-0 flex-1">
		{#if isPending}
			<span class="text-sm text-gray-500 dark:text-gray-400">Loading product data...</span>
			<span class="flex items-center gap-1 text-xs text-gray-500 dark:text-gray-400">
				<LoaderCircle size={12} class="animate-spin" />
				Fetching...
			</span>
		{:else}
			<a
				href={resolve(`/products/${product.id}`)}
				class="line-clamp-1 text-sm font-semibold text-gray-900 hover:text-brand dark:text-white dark:hover:text-brand-muted"
			>{product.name}</a>
			<p class="truncate text-xs {product.attention?.isAction ? 'font-medium text-warning' : 'text-gray-500 dark:text-gray-400'}" data-testid="mobile-product-subtitle">
				{#if product.attention}
					{attentionLabel(product.attention.kind)}
				{:else}
					{storeLabel(product)}{#if product.storeCount > 1}{` · ${product.storeCount - 1} more`}{/if}
				{/if}
			</p>
		{/if}
	</div>

	{#if !isPending}
		{#if product.sparkline && product.sparkline.length >= 3}
			<span class="hidden shrink-0 min-[400px]:block" data-testid="mobile-sparkline">
				<Sparkline data={product.sparkline} width={48} height={20} />
			</span>
		{/if}
		<div class="shrink-0 text-right">
			{#if product.currentPrice != null}
				<div class="text-sm font-semibold tabular-nums text-gray-900 dark:text-white">{formatPrice(product.currentPrice, product.currency)}</div>
				<PriceChangeChip change={product.priceChange} />
			{:else}
				<span class="text-xs text-gray-500 dark:text-gray-400">No price</span>
			{/if}
		</div>
	{/if}

	{#if onFavouriteToggle}
		<button
			type="button"
			onclick={() => onFavouriteToggle?.(product.id, !product.isFavourite)}
			class="flex min-h-11 min-w-11 shrink-0 items-center justify-center rounded-lg hover:bg-surface-2"
			aria-label={product.isFavourite ? `Remove ${product.name} from favourites` : `Add ${product.name} to favourites`}
			aria-pressed={product.isFavourite}
		>
			<Star size={16} class={product.isFavourite ? 'fill-yellow-500 text-yellow-500' : 'text-gray-300 dark:text-gray-600'} />
		</button>
	{/if}
	<ActionMenu label="Actions for {product.name}" items={menuItems} testid="row-actions" />
</div>
