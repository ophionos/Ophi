<script lang="ts">
	import {
		TrendingDown,
		TrendingUp,
		Minus,
		ExternalLink,
		Trash2,
		Bell,
		Pause,
		Play,
		LoaderCircle,
		Star,
		Check,
		X,
		History,
		RefreshCw
	} from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import { onDestroy } from 'svelte';
	import { fade } from 'svelte/transition';
	import type { Product, ProductStatus } from '$lib/api/client';
	import { formatTimeAgo, formatPrice } from '$lib/format';
	import { QuickAlert } from '$lib/utils/quickAlert.svelte';
	import { density } from '$lib/stores/density.svelte';
	import TagBadge from '$lib/components/tags/TagBadge.svelte';
	import ProductTableMobileCard from '$lib/components/products/ProductTableMobileCard.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';
	import ConvertedPrice from '$lib/components/shared/ConvertedPrice.svelte';

	interface Props {
		products: Product[];
		loading?: boolean;
		onDelete?: (id: string) => void;
		onViewHistory?: (product: Product) => void;
		onStatusChange?: (id: string, status: ProductStatus) => void;
		onFavouriteToggle?: (id: string, isFavourite: boolean) => void;
		onAlertCreated?: () => void;
		/** Bulk-selection mode: show a leading checkbox column. */
		selectable?: boolean;
		selectedIds?: ReadonlySet<string>;
		onSelectToggle?: (id: string) => void;
	}

	let { products, loading = false, onDelete, onViewHistory, onStatusChange, onFavouriteToggle, onAlertCreated, selectable = false, selectedIds, onSelectToggle }: Props = $props();

	// Density-driven cell metrics for the desktop table (mobile cards are unaffected).
	const isCompact = $derived(density.current === 'compact');
	const cellPad = $derived(isCompact ? 'px-3 py-1.5' : 'px-3 py-3');
	const avatarSize = $derived(isCompact ? 'w-7 h-7' : 'w-10 h-10');
	const nameText = $derived(isCompact ? 'text-sm' : 'text-base');

	const quickAlerts = new Map<string, QuickAlert>();

	function quickAlertFor(productId: string): QuickAlert {
		let qa = quickAlerts.get(productId);
		if (!qa) {
			qa = new QuickAlert(() => onAlertCreated?.());
			quickAlerts.set(productId, qa);
		}
		return qa;
	}

	onDestroy(() => {
		for (const qa of quickAlerts.values()) qa.destroy();
		quickAlerts.clear();
	});

	function toggleQuickAlert(product: Product) {
		quickAlertFor(product.id).toggle(product.currentPrice);
	}

	function submitQuickAlert(product: Product) {
		return quickAlertFor(product.id).submit(product.id);
	}

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
			case 'pending':
				return 'bg-gray-100 text-gray-700 dark:bg-gray-900 dark:text-gray-300';
			default:
				return 'bg-gray-100 text-gray-700 dark:bg-gray-900 dark:text-gray-300';
		}
	}
</script>

<!-- Mobile card stack -->
<div class="md:hidden flex flex-col gap-2" data-testid="product-table-mobile">
	{#if loading}
		{#each Array(3) as _, i (i)}
			<div class="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-3 animate-shimmer" data-testid="mobile-card-skeleton">
				<div class="flex gap-3">
					<div class="w-12 h-12 shrink-0 bg-gray-200 dark:bg-gray-700 rounded"></div>
					<div class="flex-1 space-y-2">
						<div class="h-4 bg-gray-200 dark:bg-gray-700 rounded w-3/4"></div>
						<div class="h-3 bg-gray-200 dark:bg-gray-700 rounded w-1/2"></div>
						<div class="h-3 bg-gray-200 dark:bg-gray-700 rounded w-1/3"></div>
					</div>
				</div>
			</div>
		{/each}
	{:else}
		{#each products as product (product.id)}
			<ProductTableMobileCard
				{product}
				{onDelete}
				{onViewHistory}
				{onStatusChange}
				{onFavouriteToggle}
				{selectable}
				selected={selectedIds?.has(product.id) ?? false}
				{onSelectToggle}
			/>
		{/each}
	{/if}
</div>

<!-- Desktop table -->
<div class="hidden md:block bg-white dark:bg-gray-800 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 overflow-hidden" data-testid="product-table">
	<div class="overflow-x-auto">
		<table class="w-full text-sm text-left">
			<thead class="text-xs text-gray-500 dark:text-gray-400 uppercase bg-gray-50 dark:bg-gray-900/50 border-b border-gray-200 dark:border-gray-700">
				<tr>
					{#if selectable}
						<th scope="col" class="px-3 py-3 w-10"><span class="sr-only">Select</span></th>
					{/if}
					<th scope="col" class="px-3 py-3 w-10"><span class="sr-only">Favourite</span></th>
					<th scope="col" class="px-3 py-3">Product</th>
					<th scope="col" class="px-3 py-3">Price</th>
					<th scope="col" class="px-3 py-3">Store</th>
					<th scope="col" class="px-3 py-3">Status</th>
					<th scope="col" class="px-3 py-3">Updated</th>
					<th scope="col" class="px-3 py-3 text-right">Actions</th>
				</tr>
			</thead>
			<tbody class="divide-y divide-gray-200 dark:divide-gray-700">
				{#if loading}
					{#each Array(5) as _, i (i)}
						<tr data-testid="desktop-row-skeleton">
							<td class="px-3 py-3"><div class="h-4 w-4 bg-gray-200 dark:bg-gray-700 rounded animate-shimmer"></div></td>
							<td class="px-3 py-3"><div class="flex items-center gap-3"><div class="w-10 h-10 bg-gray-200 dark:bg-gray-700 rounded animate-shimmer"></div><div class="h-4 bg-gray-200 dark:bg-gray-700 rounded w-32 animate-shimmer"></div></div></td>
							<td class="px-3 py-3"><div class="h-4 bg-gray-200 dark:bg-gray-700 rounded w-20 animate-shimmer"></div></td>
							<td class="px-3 py-3"><div class="h-4 bg-gray-200 dark:bg-gray-700 rounded w-8 animate-shimmer"></div></td>
							<td class="px-3 py-3"><div class="h-5 bg-gray-200 dark:bg-gray-700 rounded-full w-16 animate-shimmer"></div></td>
							<td class="px-3 py-3"><div class="h-3 bg-gray-200 dark:bg-gray-700 rounded w-20 animate-shimmer"></div></td>
							<td class="px-3 py-3"><div class="flex justify-end gap-1"><div class="h-6 w-6 bg-gray-200 dark:bg-gray-700 rounded animate-shimmer"></div><div class="h-6 w-6 bg-gray-200 dark:bg-gray-700 rounded animate-shimmer"></div></div></td>
						</tr>
					{/each}
				{/if}
				{#each products as product (product.id)}
					{@const isPending = product.status === 'pending'}
					{@const canToggleStatus = product.status === 'active' || product.status === 'paused'}
					{@const qa = quickAlertFor(product.id)}
					<tr class="hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors" in:fade={{ duration: 200 }} out:fade={{ duration: 150 }}>
						{#if selectable}
							<td class="{cellPad}">
								<input
									type="checkbox"
									checked={selectedIds?.has(product.id) ?? false}
									onchange={() => onSelectToggle?.(product.id)}
									aria-label="Select {product.name}"
									class="w-4 h-4 accent-brand cursor-pointer"
								/>
							</td>
						{/if}
						<!-- Favourite -->
						<td class="{cellPad}">
							{#if onFavouriteToggle}
								<button
									onclick={() => onFavouriteToggle?.(product.id, !product.isFavourite)}
									class="p-0.5 rounded hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors"
									aria-label={product.isFavourite ? 'Remove from favourites' : 'Add to favourites'}
								>
									<Star
										size={16}
										class={product.isFavourite
											? 'text-yellow-500 fill-yellow-500'
											: 'text-gray-400 hover:text-yellow-500'}
									/>
								</button>
							{/if}
						</td>

						<!-- Product -->
						<td class="{cellPad}">
							<div class="flex items-center gap-3">
								{#if isPending}
									<div class="{avatarSize} shrink-0 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center">
										<LoaderCircle size={14} class="animate-spin text-gray-400 dark:text-gray-500" />
									</div>
									<span class="text-gray-500 dark:text-gray-400">Loading product data...</span>
								{:else}
									{#if product.imageUrl}
										<img src={product.imageUrl} alt={product.name} loading="lazy" class="{avatarSize} shrink-0 rounded object-cover" />
									{:else}
										<div class="{avatarSize} shrink-0 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center text-gray-400 dark:text-gray-500 text-[10px]" role="img" aria-label="No image available">
											N/A
										</div>
									{/if}
									<div class="min-w-0">
										<a
											href={resolve(`/products/${product.id}`)}
											class="font-semibold {nameText} text-gray-900 dark:text-white hover:text-brand dark:hover:text-brand-muted line-clamp-1"
										>{product.name}</a>
										{#if (product.tags ?? []).length > 0}
											<div class="flex flex-wrap gap-1 mt-0.5">
												{#each (product.tags ?? []) as tag (tag.id)}
													<TagBadge name={tag.name} color={tag.color} />
												{/each}
											</div>
										{/if}
									</div>
								{/if}
							</div>
						</td>

						<!-- Price -->
						<td class="{cellPad}">
							{#if isPending}
								<span class="text-gray-500 dark:text-gray-400 flex items-center gap-1">
									<LoaderCircle size={14} class="animate-spin" />
									Fetching...
								</span>
							{:else if product.currentPrice}
								<div class="flex items-center gap-2">
									<span class="font-semibold text-gray-900 dark:text-white tabular-nums">
										{formatPrice(product.currentPrice, product.currency)}
									</span>
									<ConvertedPrice amount={product.currentPrice} currency={product.currency} />
									{#if product.priceChange}
										{@const PriceIcon = !product.priceChange || product.priceChange === 0 ? Minus : product.priceChange < 0 ? TrendingDown : TrendingUp}
										<span class="flex items-center text-xs tabular-nums {getPriceChangeColor(product.priceChange)}">
											<PriceIcon size={12} class="mr-0.5" />
											{Math.abs(product.priceChange).toFixed(1)}%
										</span>
									{/if}
								</div>
							{:else}
								<span class="text-gray-500 dark:text-gray-400">Price unavailable</span>
							{/if}
						</td>

						<!-- Store -->
						<td class="{cellPad}">
							<div class="flex items-center gap-1.5">
								<span class="text-sm text-gray-900 dark:text-white" data-testid="store-count-badge">
									{product.storeCount}
								</span>
								{#if product.alertCount > 0}
									<span class="text-xs px-1.5 py-0.5 rounded-full bg-yellow-100 dark:bg-yellow-900 text-yellow-700 dark:text-yellow-300 flex items-center gap-0.5" data-testid="alert-count-badge">
										<Bell size={10} />
										{product.alertCount}
									</span>
								{/if}
							</div>
						</td>

						<!-- Status -->
						<td class="{cellPad}">
							{#if isPending}
								<span class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium {getStatusClasses('pending')}">
									<LoaderCircle size={12} class="animate-spin" />
									Pending
								</span>
							{:else}
								<span class="inline-flex px-2 py-0.5 rounded-full text-xs font-medium {getStatusClasses(product.status)}">
									{product.status.charAt(0).toUpperCase() + product.status.slice(1)}
								</span>
							{/if}
						</td>

						<!-- Updated -->
						<td class="{cellPad} text-gray-500 dark:text-gray-400 text-xs whitespace-nowrap"
							title={product.lastChecked && !isPending ? new Date(product.lastChecked).toLocaleString() : undefined}
						>
							{#if isPending}
								Processing...
							{:else if product.lastChecked}
								{formatTimeAgo(product.lastChecked)}
							{:else}
								Never checked
							{/if}
						</td>

						<!-- Actions -->
						<td class="{cellPad}">
							<div class="flex items-center justify-end gap-1">
								<a
									href={product.affiliateUrl ?? product.url}
									target="_blank"
									rel="noopener noreferrer external"
									class="p-1.5 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded"
									aria-label="Open product in new tab"
								>
									<ExternalLink size={14} />
								</a>
								{#if !isPending && onViewHistory}
									<button
										onclick={() => onViewHistory?.(product)}
										class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
										aria-label="View price history"
									>
										<History size={14} />
									</button>
								{/if}
								{#if canToggleStatus && onStatusChange}
									<button
										onclick={() => onStatusChange?.(product.id, product.status === 'active' ? 'paused' : 'active')}
										class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
										aria-label={product.status === 'active' ? 'Pause tracking' : 'Resume tracking'}
									>
										{#if product.status === 'active'}
											<Pause size={14} />
										{:else}
											<Play size={14} />
										{/if}
									</button>
								{/if}
								{#if !isPending}
									<button
										onclick={() => toggleQuickAlert(product)}
										class="p-1.5 text-gray-400 hover:text-yellow-600 dark:hover:text-yellow-400 hover:bg-yellow-50 dark:hover:bg-yellow-900/30 rounded"
										aria-label="Set quick price alert"
									>
										<Bell size={14} />
									</button>
								{/if}
								{#if onDelete}
									<button
										onclick={() => onDelete?.(product.id)}
										class="p-1.5 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded"
										aria-label="Delete product"
									>
										<Trash2 size={14} />
									</button>
								{/if}
							</div>
						</td>
					</tr>

					<!-- Quick Alert Row -->
					{#if qa.open}
						<tr class="bg-gray-50 dark:bg-gray-900/30">
							<td colspan={selectable ? 8 : 7} class="px-3 py-2">
								<div role="region" aria-label="Quick price alert for {product.name}">
									{#if qa.success}
										<p class="text-sm text-green-600 dark:text-green-400">Alert set ✓</p>
									{:else}
										<div class="flex items-center gap-2">
											<label for="quick-alert-price-{product.id}" class="text-xs text-gray-500 dark:text-gray-400 whitespace-nowrap">Alert below</label>
											<span class="text-xs text-gray-500 dark:text-gray-400">{product.currency}</span>
											<input
												id="quick-alert-price-{product.id}"
												type="number"
												step="0.01"
												min="0"
												bind:value={qa.price}
												disabled={qa.loading}
												class="w-24 px-2 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
											/>
											<button
												onclick={() => submitQuickAlert(product)}
												disabled={qa.loading}
												class="p-1 text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30 rounded disabled:opacity-50"
												aria-label="Create alert"
											>
												<Check size={16} />
											</button>
											<button
												onclick={() => toggleQuickAlert(product)}
												class="p-1 text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 rounded"
												aria-label="Cancel"
											>
												<X size={16} />
											</button>
										</div>
										{#if qa.error}
											<FormError size="xs" class="mt-1" message={qa.error}>
												{#if qa.isNetworkError}
													<button
														onclick={() => submitQuickAlert(product)}
														class="text-xs text-brand dark:text-brand-muted hover:underline flex items-center gap-1"
														aria-label="Retry creating alert"
													>
														<RefreshCw size={12} />
														Retry
													</button>
												{/if}
											</FormError>
										{/if}
									{/if}
								</div>
							</td>
						</tr>
					{/if}
				{/each}
			</tbody>
		</table>
	</div>
</div>
