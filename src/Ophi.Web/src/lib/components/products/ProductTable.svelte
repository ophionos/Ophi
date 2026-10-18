<script lang="ts">
	import {
		LoaderCircle,
		Star,
		Check,
		X,
		RefreshCw,
		ArrowUp,
		ArrowDown,
		ArrowUpDown
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
	import ActionMenu from '$lib/components/shared/ActionMenu.svelte';
	import { productMenuItems } from '$lib/components/products/productMenu';
	import AttentionPill from '$lib/components/products/AttentionPill.svelte';
	import PriceChangeChip from '$lib/components/products/PriceChangeChip.svelte';
	import PriceRangeBar from '$lib/components/products/PriceRangeBar.svelte';
	import Sparkline from '$lib/components/products/Sparkline.svelte';
	import {
		alertDistanceText,
		alertTargetText,
		isPriceOld,
		productInitial,
		storeLabel
	} from '$lib/utils/watchlist';

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
		/** The list's sort; the sortable headers show and change it. */
		sortBy?: string;
		sortDirection?: string;
		onSortChange?: (sortBy: string, sortDirection: string) => void;
	}

	let {
		products,
		loading = false,
		onDelete,
		onViewHistory,
		onStatusChange,
		onFavouriteToggle,
		onAlertCreated,
		selectable = false,
		selectedIds,
		onSelectToggle,
		sortBy = '',
		sortDirection = 'asc',
		onSortChange
	}: Props = $props();

	// Density-driven cell metrics for the desktop table (mobile cards are unaffected).
	const isCompact = $derived(density.current === 'compact');
	const cellPad = $derived(isCompact ? 'px-3 py-1.5' : 'px-3 py-3');
	const avatarSize = $derived(isCompact ? 'w-7 h-7' : 'w-10 h-10');
	const nameText = $derived(isCompact ? 'text-sm' : 'text-base');

	// The direction a column sorts in when it is first chosen.
	const SORTABLE: Record<string, string> = { name: 'asc', price: 'asc', pricechange: 'asc', lastchecked: 'desc' };
	const columnCount = $derived(selectable ? 10 : 9);

	// The failure count only explains a scrape problem; out of stock or paused have none to show.
	const FAILURE_KINDS = new Set(['failing', 'blocked', 'priceNotFound']);

	function ariaSort(column: string): 'ascending' | 'descending' | 'none' {
		if (sortBy !== column) return 'none';
		return sortDirection === 'desc' ? 'descending' : 'ascending';
	}

	function sortOn(column: string) {
		if (!onSortChange) return;
		if (sortBy === column) onSortChange(column, sortDirection === 'asc' ? 'desc' : 'asc');
		else onSortChange(column, SORTABLE[column]);
	}

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

	function menuItems(product: Product) {
		return productMenuItems(product, { onViewHistory, onStatusChange, onSetAlert: toggleQuickAlert, onDelete });
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

{#snippet sortHeader(column: string, label: string, align: 'left' | 'right' = 'left')}
	<th scope="col" class="px-3 py-3 {align === 'right' ? 'text-right' : ''}" aria-sort={ariaSort(column)}>
		{#if onSortChange}
			{@const SortIcon = sortBy !== column ? ArrowUpDown : sortDirection === 'desc' ? ArrowDown : ArrowUp}
			<button
				type="button"
				onclick={() => sortOn(column)}
				class="inline-flex items-center gap-1 uppercase hover:text-gray-900 dark:hover:text-white {sortBy === column ? 'text-gray-900 dark:text-white' : ''}"
				data-testid="sort-header-{column}"
			>
				{label}
				<SortIcon size={12} aria-hidden="true" class={sortBy === column ? '' : 'opacity-40'} />
			</button>
		{:else}
			{label}
		{/if}
	</th>
{/snippet}

<!-- Desktop table -->
<div class="hidden md:block bg-surface-1 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 overflow-hidden" data-testid="product-table">
	<div class="overflow-x-auto">
		<table class="w-full text-sm text-left">
			<thead class="text-xs text-gray-500 dark:text-gray-400 uppercase bg-surface-2 border-b border-gray-200 dark:border-gray-700">
				<tr>
					{#if selectable}
						<th scope="col" class="px-3 py-3 w-10"><span class="sr-only">Select</span></th>
					{/if}
					{@render sortHeader('name', 'Product')}
					<th scope="col" class="px-3 py-3">Status</th>
					{@render sortHeader('price', 'Price', 'right')}
					{@render sortHeader('pricechange', 'Change')}
					<th scope="col" class="px-3 py-3 whitespace-nowrap">30 days</th>
					<th scope="col" class="px-3 py-3 whitespace-nowrap">90-day range</th>
					<th scope="col" class="px-3 py-3">Alert</th>
					{@render sortHeader('lastchecked', 'Checked')}
					<th scope="col" class="px-3 py-3 w-12"><span class="sr-only">Actions</span></th>
				</tr>
			</thead>
			<tbody class="divide-y divide-gray-200 dark:divide-gray-700">
				{#if loading}
					{#each Array(5) as _, i (i)}
						<tr data-testid="desktop-row-skeleton">
							{#each Array(columnCount) as _, c (c)}
								<td class="px-3 py-3"><div class="h-4 bg-surface-3 rounded animate-shimmer {c === 0 ? 'w-40' : 'w-12'}"></div></td>
							{/each}
						</tr>
					{/each}
				{/if}
				{#each products as product (product.id)}
					{@const isPending = product.status === 'pending'}
					{@const qa = quickAlertFor(product.id)}
					{@const oldPrice = isPriceOld(product)}
					{@const store = storeLabel(product)}
					{@const failures = product.attention && FAILURE_KINDS.has(product.attention.kind) ? product.attention.consecutiveFailures : 0}
					<tr class="hover:bg-surface-2 transition-colors" in:fade={{ duration: 200 }} out:fade={{ duration: 150 }} data-testid="product-row">
						{#if selectable}
							<td class={cellPad}>
								<input
									type="checkbox"
									checked={selectedIds?.has(product.id) ?? false}
									onchange={() => onSelectToggle?.(product.id)}
									aria-label="Select {product.name}"
									class="w-4 h-4 cursor-pointer"
								/>
							</td>
						{/if}

						<!-- Product -->
						<td class="{cellPad} min-w-56">
							<div class="flex items-center gap-3">
								{#if isPending}
									<div class="{avatarSize} shrink-0 bg-surface-2 rounded flex items-center justify-center">
										<LoaderCircle size={14} class="animate-spin text-gray-400 dark:text-gray-500" />
									</div>
									<span class="text-gray-500 dark:text-gray-400">Loading product data...</span>
								{:else}
									{#if product.imageUrl}
										<img src={product.imageUrl} alt="" loading="lazy" class="{avatarSize} shrink-0 rounded object-cover" />
									{:else}
										<div class="{avatarSize} shrink-0 bg-surface-2 rounded flex items-center justify-center font-semibold text-gray-500 dark:text-gray-400" aria-hidden="true" data-testid="product-initial">
											{productInitial(product.name)}
										</div>
									{/if}
									<div class="min-w-0">
										<div class="flex items-center gap-1">
											<a
												href={resolve(`/products/${product.id}`)}
												class="font-semibold {nameText} text-gray-900 dark:text-white hover:text-brand dark:hover:text-brand-muted line-clamp-1"
											>{product.name}</a>
											{#if onFavouriteToggle}
												<button
													type="button"
													onclick={() => onFavouriteToggle?.(product.id, !product.isFavourite)}
													class="shrink-0 p-0.5 rounded hover:bg-surface-2"
													aria-label={product.isFavourite ? `Remove ${product.name} from favourites` : `Add ${product.name} to favourites`}
													aria-pressed={product.isFavourite}
												>
													<Star size={14} class={product.isFavourite ? 'text-yellow-500 fill-yellow-500' : 'text-gray-300 hover:text-yellow-500 dark:text-gray-600'} />
												</button>
											{:else if product.isFavourite}
												<Star size={14} class="shrink-0 text-yellow-500 fill-yellow-500" aria-label="Favourite" />
											{/if}
										</div>
										<p class="text-xs text-gray-500 dark:text-gray-400" data-testid="product-store">
											{store}{#if product.storeCount > 1}{` · ${product.storeCount - 1} more`}{/if}
										</p>
										{#if (product.tags ?? []).length > 0}
											<div class="flex flex-wrap gap-1 mt-0.5">
												{#each product.tags ?? [] as tag (tag.id)}
													<TagBadge name={tag.name} color={tag.color} />
												{/each}
											</div>
										{/if}
									</div>
								{/if}
							</div>
						</td>

						<!-- Status: empty while the product is healthy -->
						<td class={cellPad}>
							{#if isPending}
								<span class="inline-flex items-center gap-1 text-xs text-gray-500 dark:text-gray-400">
									<LoaderCircle size={12} class="animate-spin" />
									Pending
								</span>
							{:else if product.attention}
								<AttentionPill attention={product.attention} productId={product.id} />
							{/if}
						</td>

						<!-- Price -->
						<td class="{cellPad} text-right whitespace-nowrap">
							{#if isPending}
								<span class="text-gray-500 dark:text-gray-400">Fetching...</span>
							{:else if product.currentPrice != null}
								<div class="font-semibold tabular-nums {oldPrice ? 'text-gray-400 dark:text-gray-500' : 'text-gray-900 dark:text-white'}" data-testid="product-price">
									{formatPrice(product.currentPrice, product.currency)}
								</div>
								<div class="text-xs text-gray-500 dark:text-gray-400">
									{#if oldPrice}
										<span title={product.lastSucceededAt ? `Last read ${new Date(product.lastSucceededAt).toLocaleString()}` : undefined} data-testid="old-price">old price</span>
									{:else}
										{product.currency}
									{/if}
								</div>
							{:else}
								<span class="text-gray-500 dark:text-gray-400">No price</span>
							{/if}
						</td>

						<!-- Change -->
						<td class={cellPad}>
							{#if !isPending}
								<PriceChangeChip change={product.priceChange} />
							{/if}
						</td>

						<!-- 30 days -->
						<td class={cellPad}>
							{#if product.sparkline && product.sparkline.length >= 3}
								<Sparkline data={product.sparkline} width={72} height={22} />
							{/if}
						</td>

						<!-- 90-day range -->
						<td class={cellPad}>
							{#if product.priceMin != null && product.priceMax != null && product.currentPrice != null}
								<PriceRangeBar min={product.priceMin} max={product.priceMax} current={product.currentPrice} currency={product.currency} />
							{/if}
						</td>

						<!-- Alert -->
						<td class="{cellPad} whitespace-nowrap" data-testid="product-alert">
							{#if product.closestAlert}
								<div class="tabular-nums text-gray-900 dark:text-white">{alertTargetText(product.closestAlert, product.currency)}</div>
								<div class="text-xs tabular-nums {product.closestAlert.isNear ? 'font-medium text-brand dark:text-brand-muted' : 'text-gray-500 dark:text-gray-400'}">
									{alertDistanceText(product.closestAlert, product.currency)}
								</div>
							{/if}
						</td>

						<!-- Checked -->
						<td class="{cellPad} text-xs whitespace-nowrap text-gray-500 dark:text-gray-400"
							title={product.lastChecked && !isPending ? new Date(product.lastChecked).toLocaleString() : undefined}
						>
							{#if isPending}
								Processing...
							{:else if product.lastChecked}
								{formatTimeAgo(product.lastChecked)}
							{:else}
								Never
							{/if}
							{#if failures > 0}
								<div class="text-warning" data-testid="failed-runs">{failures} failed {failures === 1 ? 'run' : 'runs'}</div>
							{/if}
						</td>

						<!-- Actions -->
						<td class="{cellPad} text-right">
							<ActionMenu label="Actions for {product.name}" items={menuItems(product)} testid="row-actions" />
						</td>
					</tr>

					<!-- Quick Alert Row -->
					{#if qa.open}
						<tr class="bg-surface-2">
							<td colspan={columnCount} class="px-3 py-2">
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
												class="w-24 px-2 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded bg-surface-1 text-gray-900 dark:text-white"
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
												class="p-1 text-gray-400 hover:bg-surface-3 rounded"
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
