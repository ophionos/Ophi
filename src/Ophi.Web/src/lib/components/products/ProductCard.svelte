<script lang="ts">
	import { LoaderCircle, Star, Check, X, RefreshCw } from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import { onDestroy } from 'svelte';
	import type { Product, ProductStatus } from '$lib/api/client';
	import { formatTimeAgo, formatPrice, formatDateTime } from '$lib/format';
	import TagBadge from '$lib/components/tags/TagBadge.svelte';
	import Sparkline from '$lib/components/products/Sparkline.svelte';
	import AttentionIcon from '$lib/components/products/AttentionIcon.svelte';
	import SevenDayChange from '$lib/components/products/SevenDayChange.svelte';
	import ActionMenu from '$lib/components/shared/ActionMenu.svelte';
	import { productMenuItems } from '$lib/components/products/productMenu';
	import { QuickAlert } from '$lib/utils/quickAlert.svelte';
	import {
		PENDING_TEXT,
		alertDistanceText,
		alertTargetText,
		isPriceOld,
		productInitial,
		rangeTitle,
		storeLine,
		trackedText
	} from '$lib/utils/watchlist';
	import FormError from '$lib/components/shared/FormError.svelte';
	import ConvertedPrice from '$lib/components/shared/ConvertedPrice.svelte';

	interface Props {
		product: Product;
		onDelete?: (id: string) => void;
		onViewHistory?: (product: Product) => void;
		onStatusChange?: (id: string, status: ProductStatus) => void;
		onFavouriteToggle?: (id: string, isFavourite: boolean) => void;
		onAlertCreated?: () => void;
		/** Bulk-selection mode: show a leading checkbox. */
		selectable?: boolean;
		selected?: boolean;
		onSelectToggle?: (id: string) => void;
	}

	let { product, onDelete, onViewHistory, onStatusChange, onFavouriteToggle, onAlertCreated, selectable = false, selected = false, onSelectToggle }: Props = $props();

	const isPending = $derived(product.status === 'pending');
	const oldPrice = $derived(isPriceOld(product));

	const quickAlert = new QuickAlert(() => onAlertCreated?.());
	onDestroy(() => quickAlert.destroy());

	function toggleQuickAlert() {
		quickAlert.toggle(product.currentPrice);
	}

	function submitQuickAlert() {
		return quickAlert.submit(product.id);
	}

	const menuItems = $derived(productMenuItems(product, { onViewHistory, onStatusChange, onSetAlert: toggleQuickAlert, onDelete }));
</script>

<!-- The gallery card shows the same facts as a table row, in a picture-first layout. A product
     without an image gets no placeholder area: the card starts with its name. -->
<div class="relative flex h-full flex-col overflow-hidden rounded-lg border border-gray-200 bg-surface-1 dark:border-gray-700" data-testid="product-card">
	{#if isPending}
		<div class="aspect-16/9 w-full animate-shimmer bg-surface-2" aria-hidden="true"></div>
	{:else if product.imageUrl}
		<a href={resolve(`/products/${product.id}`)} class="block aspect-16/9 w-full overflow-hidden bg-surface-2" tabindex="-1" aria-hidden="true">
			<img src={product.imageUrl} alt="" loading="lazy" class="h-full w-full object-cover" />
		</a>
	{/if}

	<div class="relative flex flex-1 flex-col gap-2 p-3">
		<div class="flex items-start gap-2">
			{#if selectable}
				<label class="-my-2 -ml-2 flex min-h-11 min-w-11 shrink-0 cursor-pointer items-center justify-center">
					<input
						type="checkbox"
						checked={selected}
						onchange={() => onSelectToggle?.(product.id)}
						aria-label="Select {product.name}"
						class="h-4 w-4 cursor-pointer"
					/>
				</label>
			{/if}
			{#if !isPending && !product.imageUrl}
				<div class="flex h-8 w-8 shrink-0 items-center justify-center rounded bg-surface-2 text-sm font-semibold text-gray-500 dark:text-gray-400" aria-hidden="true" data-testid="product-initial">
					{productInitial(product.name)}
				</div>
			{/if}
			<div class="min-w-0 flex-1">
				{#if isPending}
					<span class="text-sm font-medium text-gray-500 dark:text-gray-400">{PENDING_TEXT}</span>
				{:else}
					<div class="flex items-start gap-1">
						<a
							href={resolve(`/products/${product.id}`)}
							class="line-clamp-2 font-semibold leading-snug text-gray-900 hover:text-brand dark:text-white dark:hover:text-brand-muted"
							title={product.name}
						>{product.name}</a>
						{#if product.attention}
							<span class="mt-0.5"><AttentionIcon attention={product.attention} productId={product.id} /></span>
						{/if}
					</div>
					<p class="truncate text-xs text-gray-500 dark:text-gray-400" data-testid="product-store">
						{storeLine(product)}
					</p>
				{/if}
			</div>
			{#if onFavouriteToggle}
				<button
					type="button"
					onclick={() => onFavouriteToggle?.(product.id, !product.isFavourite)}
					class="-my-2 -mr-2 flex min-h-11 min-w-11 shrink-0 items-center justify-center rounded-lg hover:bg-surface-2"
					aria-label={product.isFavourite ? `Remove ${product.name} from favourites` : `Add ${product.name} to favourites`}
					aria-pressed={product.isFavourite}
				>
					<Star size={16} class={product.isFavourite ? 'fill-yellow-500 text-yellow-500' : 'text-gray-300 hover:text-yellow-500 dark:text-gray-600'} />
				</button>
			{/if}
		</div>

		<div class="flex items-end justify-between gap-2">
			<div class="flex flex-wrap items-center gap-2">
				{#if isPending}
					<span class="flex items-center gap-2 text-sm text-gray-500 dark:text-gray-400">
						<LoaderCircle size={14} class="animate-spin" />
						{PENDING_TEXT}
					</span>
				{:else if product.currentPrice != null}
					<span class="text-lg font-bold leading-none tabular-nums {oldPrice ? 'text-gray-400 dark:text-gray-500' : 'text-gray-900 dark:text-white'}" data-testid="product-price">
						{formatPrice(product.currentPrice, product.currency)}
					</span>
					<ConvertedPrice amount={product.currentPrice} currency={product.currency} />
				{:else}
					<span class="text-sm text-gray-500 dark:text-gray-400">No price</span>
				{/if}
			</div>
			<!-- 3+ points: a shorter series is only a stub, so a young product shows its age instead. -->
			{#if product.sparkline && product.sparkline.length >= 3}
				<span title={rangeTitle(product)} data-testid="price-sparkline">
					<Sparkline data={product.sparkline} width={72} height={22} />
				</span>
			{:else if trackedText(product)}
				<span class="text-xs text-gray-500 dark:text-gray-400" data-testid="tracked-days">{trackedText(product)}</span>
			{/if}
		</div>

		{#if !isPending}
			<div><SevenDayChange {product} /></div>
		{/if}

		{#if product.closestAlert}
			<p class="text-xs tabular-nums text-gray-500 dark:text-gray-400" data-testid="product-alert">
				Alert {alertTargetText(product.closestAlert, product.currency)} ·
				<span class={product.closestAlert.isNear ? 'font-medium text-brand dark:text-brand-muted' : ''}>{alertDistanceText(product.closestAlert, product.currency)}</span>
			</p>
		{/if}

		{#if (product.tags ?? []).length > 0}
			<div class="flex flex-wrap items-center gap-1">
				{#each product.tags ?? [] as tag (tag.id)}
					<TagBadge name={tag.name} color={tag.color} />
				{/each}
			</div>
		{/if}

		<div class="mt-auto -mb-2 -mr-2 flex items-center justify-between border-t border-gray-100 pt-1 dark:border-gray-700">
			<span class="text-xs text-gray-500 dark:text-gray-400" title={product.lastChecked ? formatDateTime(product.lastChecked) : undefined}>
				{#if isPending}
					{PENDING_TEXT}
				{:else if product.lastChecked}
					Checked {formatTimeAgo(product.lastChecked)}
				{:else}
					Never checked
				{/if}
			</span>
			<ActionMenu label="Actions for {product.name}" items={menuItems} testid="row-actions" />
		</div>

		<!-- Quick alert overlay — over the card content so it doesn't change the card's height -->
		{#if quickAlert.open}
			<div class="absolute inset-x-0 bottom-0 z-20 border-t border-gray-200 bg-surface-1 px-3 py-2 dark:border-gray-700" role="region" aria-label="Quick price alert for {product.name}">
				{#if quickAlert.success}
					<p class="py-1 text-sm text-success">Alert set ✓</p>
				{:else}
					<div class="flex items-center gap-2">
						<label for="quick-alert-price-{product.id}" class="whitespace-nowrap text-xs text-gray-500 dark:text-gray-400">Alert below</label>
						<div class="flex flex-1 items-center gap-1">
							<span class="text-xs text-gray-500 dark:text-gray-400">{product.currency}</span>
							<input
								id="quick-alert-price-{product.id}"
								type="number"
								step="0.01"
								min="0"
								bind:value={quickAlert.price}
								disabled={quickAlert.loading}
								class="w-24 rounded border border-gray-300 bg-white px-2 py-1 text-sm text-gray-900 dark:border-gray-600 dark:bg-gray-700 dark:text-white"
							/>
						</div>
						<button
							type="button"
							onclick={submitQuickAlert}
							disabled={quickAlert.loading}
							class="flex min-h-11 min-w-11 items-center justify-center rounded text-success hover:bg-success/10 disabled:opacity-50"
							aria-label="Create alert"
						>
							<Check size={16} />
						</button>
						<button
							type="button"
							onclick={toggleQuickAlert}
							class="flex min-h-11 min-w-11 items-center justify-center rounded text-gray-400 hover:bg-surface-2"
							aria-label="Cancel"
						>
							<X size={16} />
						</button>
					</div>
					{#if quickAlert.error}
						<FormError size="xs" class="mt-1" message={quickAlert.error}>
							{#if quickAlert.isNetworkError}
								<button
									type="button"
									onclick={submitQuickAlert}
									class="flex items-center gap-1 text-xs text-brand hover:underline dark:text-brand-muted"
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
		{/if}
	</div>
</div>
