<script lang="ts">
	import {
		TrendingDown,
		TrendingUp,
		ExternalLink,
		Trash2,
		Bell,
		Pause,
		Play,
		LoaderCircle,
		Star,
		History
	} from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import type { Product, ProductStatus } from '$lib/api/client';
	import { formatTimeAgo, formatPrice } from '$lib/format';
	import TagBadge from '$lib/components/tags/TagBadge.svelte';
	import ConvertedPrice from '$lib/components/shared/ConvertedPrice.svelte';

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
	const canToggleStatus = $derived(product.status === 'active' || product.status === 'paused');

	function getPriceChangeColor(priceChange?: number) {
		if (!priceChange || priceChange === 0) return 'text-gray-500';
		return priceChange < 0 ? 'text-green-600' : 'text-red-600';
	}

	function getStatusClasses(status: string) {
		switch (status) {
			case 'active':
				return 'bg-green-100 text-green-700 dark:bg-green-900 dark:text-green-300';
			case 'paused':
				return 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900 dark:text-yellow-300';
			case 'error':
				return 'bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300';
			default:
				return 'bg-gray-100 text-gray-700 dark:bg-gray-900 dark:text-gray-300';
		}
	}
</script>

<div class="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-3" data-testid="mobile-product-card">
	<div class="flex gap-3">
		{#if selectable}
			<input
				type="checkbox"
				checked={selected}
				onchange={() => onSelectToggle?.(product.id)}
				aria-label="Select {product.name}"
				class="self-center w-4 h-4 shrink-0 accent-brand cursor-pointer"
			/>
		{/if}
		<!-- Thumbnail -->
		{#if isPending}
			<div class="w-12 h-12 shrink-0 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center">
				<LoaderCircle size={16} class="animate-spin text-gray-400" />
			</div>
		{:else if product.imageUrl}
			<a href={resolve(`/products/${product.id}`)} class="shrink-0">
				<img src={product.imageUrl} alt={product.name} loading="lazy" class="w-12 h-12 rounded object-cover" />
			</a>
		{:else}
			<div class="w-12 h-12 shrink-0 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center text-gray-400 dark:text-gray-500 text-[10px]" role="img" aria-label="No image available">
				N/A
			</div>
		{/if}

		<!-- Main content -->
		<div class="flex-1 min-w-0">
			<div class="flex items-start justify-between gap-2">
				<div class="min-w-0">
					{#if isPending}
						<span class="text-sm text-gray-500 dark:text-gray-400">Loading product data...</span>
					{:else}
						<a
							href={resolve(`/products/${product.id}`)}
							class="font-semibold text-sm text-gray-900 dark:text-white hover:text-brand dark:hover:text-brand-muted line-clamp-1"
						>{product.name}</a>
					{/if}
				</div>
				{#if onFavouriteToggle}
					<button
						onclick={() => onFavouriteToggle?.(product.id, !product.isFavourite)}
						class="p-0.5 shrink-0"
						aria-label={product.isFavourite ? 'Remove from favourites' : 'Add to favourites'}
					>
						<Star
							size={14}
							class={product.isFavourite
								? 'text-yellow-500 fill-yellow-500'
								: 'text-gray-400'}
						/>
					</button>
				{/if}
			</div>

			<!-- Price + Status row -->
			<div class="flex items-center gap-2 mt-1">
				{#if isPending}
					<span class="text-xs text-gray-500 dark:text-gray-400 flex items-center gap-1">
						<LoaderCircle size={12} class="animate-spin" />
						Fetching...
					</span>
				{:else if product.currentPrice}
					<span class="text-sm font-semibold text-gray-900 dark:text-white tabular-nums">
						{formatPrice(product.currentPrice, product.currency)}
					</span>
					<ConvertedPrice amount={product.currentPrice} currency={product.currency} />
					{#if product.priceChange}
						{@const PriceIcon = product.priceChange < 0 ? TrendingDown : TrendingUp}
						<span class="flex items-center text-xs tabular-nums {getPriceChangeColor(product.priceChange)}">
							<PriceIcon size={10} class="mr-0.5" />
							{Math.abs(product.priceChange).toFixed(1)}%
						</span>
					{/if}
				{:else}
					<span class="text-xs text-gray-500 dark:text-gray-400">Price unavailable</span>
				{/if}

				<span class="inline-flex px-1.5 py-0.5 rounded-full text-[10px] font-medium {getStatusClasses(product.status)}">
					{product.status.charAt(0).toUpperCase() + product.status.slice(1)}
				</span>
			</div>

			<!-- Tags -->
			{#if (product.tags ?? []).length > 0}
				<div class="flex flex-wrap gap-1 mt-1">
					{#each (product.tags ?? []) as tag (tag.id)}
						<TagBadge name={tag.name} color={tag.color} />
					{/each}
				</div>
			{/if}

			<!-- Updated + Actions row -->
			<div class="flex items-center justify-between mt-2">
				<span class="text-[10px] text-gray-500 dark:text-gray-400">
					{#if isPending}
						Processing...
					{:else if product.lastChecked}
						{formatTimeAgo(product.lastChecked)}
					{:else}
						Never checked
					{/if}
				</span>

				<div class="flex items-center gap-1">
					<a
						href={product.affiliateUrl ?? product.url}
						target="_blank"
						rel="noopener noreferrer external"
						class="p-1.5 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 rounded min-w-10 min-h-10 flex items-center justify-center"
						aria-label="Open product in new tab"
					>
						<ExternalLink size={14} />
					</a>
					{#if !isPending && onViewHistory}
						<button
							onclick={() => onViewHistory?.(product)}
							class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted rounded min-w-10 min-h-10 flex items-center justify-center"
							aria-label="View price history"
						>
							<History size={14} />
						</button>
					{/if}
					{#if canToggleStatus && onStatusChange}
						<button
							onclick={() => onStatusChange?.(product.id, product.status === 'active' ? 'paused' : 'active')}
							class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted rounded min-w-10 min-h-10 flex items-center justify-center"
							aria-label={product.status === 'active' ? 'Pause tracking' : 'Resume tracking'}
						>
							{#if product.status === 'active'}
								<Pause size={14} />
							{:else}
								<Play size={14} />
							{/if}
						</button>
					{/if}
					{#if onDelete}
						<button
							onclick={() => onDelete?.(product.id)}
							class="p-1.5 text-gray-400 hover:text-red-600 dark:hover:text-red-400 rounded min-w-10 min-h-10 flex items-center justify-center"
							aria-label="Delete product"
						>
							<Trash2 size={14} />
						</button>
					{/if}
				</div>
			</div>
		</div>
	</div>
</div>
