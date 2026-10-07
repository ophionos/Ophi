<script lang="ts">
	import {
		TrendingDown,
		TrendingUp,
		ExternalLink,
		Trash2,
		Bell,
		Store,
		Pause,
		Play,
		Clock,
		LoaderCircle,
		Star,
		Check,
		X,
		PackageX,
		RefreshCw,
		TriangleAlert,
		ImageOff
	} from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import { onDestroy } from 'svelte';
	import type { Product, ProductStatus } from '$lib/api/client';
	import { formatTimeAgo, formatPrice } from '$lib/format';
	import TagBadge from '$lib/components/tags/TagBadge.svelte';
	import Sparkline from '$lib/components/products/Sparkline.svelte';
	import PricePositionBar from '$lib/components/products/PricePositionBar.svelte';
	import DealScore from '$lib/components/products/DealScore.svelte';
	import { createAnimatedPrice } from '$lib/utils/animatedPrice.svelte';
	import { getPriceStory } from '$lib/utils/priceStory';
	import { QuickAlert } from '$lib/utils/quickAlert.svelte';
	import ScanAnimation from '$lib/components/products/ScanAnimation.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';
	import ConvertedPrice from '$lib/components/shared/ConvertedPrice.svelte';

	interface Props {
		product: Product;
		onDelete?: (id: string) => void;
		onViewHistory?: (product: Product) => void;
		onStatusChange?: (id: string, status: ProductStatus) => void;
		onFavouriteToggle?: (id: string, isFavourite: boolean) => void;
		onAlertCreated?: () => void;
		/** Bulk-selection mode: show a checkbox overlay. */
		selectable?: boolean;
		selected?: boolean;
		onSelectToggle?: (id: string) => void;
	}

	let { product, onDelete, onViewHistory, onStatusChange, onFavouriteToggle, onAlertCreated, selectable = false, selected = false, onSelectToggle }: Props = $props();

	const isPending = $derived(product.status === 'pending');
	const canToggleStatus = $derived(product.status === 'active' || product.status === 'paused');

	const animatedPrice = createAnimatedPrice(0);

	$effect(() => {
		animatedPrice.target = product.currentPrice ?? 0;
	});

	onDestroy(() => animatedPrice.destroy());

	const quickAlert = new QuickAlert(() => onAlertCreated?.());
	onDestroy(() => quickAlert.destroy());

	function toggleQuickAlert() {
		quickAlert.toggle(product.currentPrice);
	}

	function submitQuickAlert() {
		return quickAlert.submit(product.id);
	}

	const priceStory = $derived(getPriceStory({
		currentPrice: product.currentPrice,
		previousPrice: product.previousPrice,
		priceChange: product.priceChange,
		priceMin: product.priceMin,
		priceMax: product.priceMax,
		sparkline: product.sparkline
	}));

	const storyVariantClasses: Record<string, string> = {
		positive: 'bg-green-100 dark:bg-green-900/40 text-green-700 dark:text-green-400',
		negative: 'bg-red-100 dark:bg-red-900/40 text-red-700 dark:text-red-400',
		neutral: 'bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400',
		highlight: 'bg-brand-subtle dark:bg-brand-dark/40 text-brand dark:text-brand-muted'
	};

	function getPriceChangeColor() {
		if (!product.priceChange || product.priceChange === 0) return 'text-gray-500';
		return product.priceChange < 0 ? 'text-green-600' : 'text-red-600';
	}

	function getPriceChangeBgColor() {
		if (!product.priceChange || product.priceChange === 0) return 'bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400';
		return product.priceChange < 0
			? 'bg-green-100 dark:bg-green-900/40 text-green-700 dark:text-green-400'
			: 'bg-red-100 dark:bg-red-900/40 text-red-700 dark:text-red-400';
	}


</script>

<div
	class="bg-white/90 dark:bg-gray-800/80 backdrop-blur-md rounded-xl shadow-sm border border-gray-200 dark:border-white/5 overflow-hidden transition-all duration-300 ease-out hover:shadow-xl hover:shadow-brand/10 hover:-translate-y-1 hover:scale-[1.01] flex flex-col h-full ring-1 ring-transparent hover:ring-black/5 dark:hover:ring-white/10 group relative"
	data-testid="product-card"
>
	<!-- Price position bar -->
	{#if product.priceMin != null && product.priceMax != null && product.currentPrice != null && product.priceMin !== product.priceMax}
		<PricePositionBar min={product.priceMin} max={product.priceMax} current={product.currentPrice} />
	{/if}
	{#if selectable}
		<label class="absolute top-2 left-2 z-20 p-1.5 bg-white/90 dark:bg-gray-900/80 rounded-md shadow-sm cursor-pointer">
			<input
				type="checkbox"
				checked={selected}
				onchange={() => onSelectToggle?.(product.id)}
				aria-label="Select {product.name}"
				class="block w-4 h-4 accent-brand cursor-pointer"
			/>
		</label>
	{/if}
	<!-- Optional: Subtle inner glow on hover -->
	<div class="absolute inset-0 bg-gradient-to-br from-white/40 to-transparent dark:from-white/5 dark:to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-300 pointer-events-none"></div>
	<!-- Image area — 16/9 when there's a visual; a short strip when there's no image so
	     image-less cards (e.g. manually-added products) stay compact rather than reserving
	     a full-height placeholder. -->
	<div
		class="w-full {isPending || product.imageUrl
			? 'aspect-16/9'
			: 'h-20'} bg-gray-100 dark:bg-gray-700 relative overflow-hidden"
	>
		<a href={resolve(`/products/${product.id}`)} class="absolute inset-0">
			{#if isPending}
				<ScanAnimation />
			{:else if product.imageUrl}
				<img src={product.imageUrl} alt={product.name} loading="lazy" class="w-full h-full object-cover" />
				<!-- Gradient overlay for text readability -->
				<div class="absolute inset-x-0 bottom-0 h-16 bg-linear-to-t from-black/20 to-transparent pointer-events-none"></div>
			{:else}
				<div class="w-full h-full flex items-center justify-center text-gray-300 dark:text-gray-600" role="img" aria-label="No image available for {product.name}">
					<ImageOff size={20} />
				</div>
			{/if}
		</a>
		<!-- Favourite + Deal Score overlay -->
		<div class="absolute top-2 left-2 flex items-center gap-1 z-10">
			{#if product.dealScore != null}
				<div class="p-1 rounded-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm shadow-sm text-gray-700 dark:text-gray-200" title="Deal score: {product.dealScore}/100">
					<DealScore score={product.dealScore} />
				</div>
			{/if}
			{#if onFavouriteToggle}
				<button
					onclick={() => onFavouriteToggle?.(product.id, !product.isFavourite)}
					class="p-1.5 rounded-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm hover:bg-white dark:hover:bg-gray-800 transition-colors shadow-sm"
					aria-label={product.isFavourite ? 'Remove from favourites' : 'Add to favourites'}
				>
					<Star
						size={14}
						class={product.isFavourite
							? 'text-yellow-500 fill-yellow-500'
							: 'text-gray-400 hover:text-yellow-500'}
					/>
				</button>
			{/if}
		</div>
		<!-- Out of stock / anomaly badges -->
		<div class="absolute bottom-2 left-2 z-10 flex flex-col gap-1">
			{#if product.isOutOfStock}
				<span class="text-xs font-semibold px-2 py-1 rounded-full bg-red-600/90 text-white backdrop-blur-sm shadow-sm flex items-center gap-1" data-testid="out-of-stock-badge">
					<PackageX size={12} />
					Out of Stock
				</span>
			{/if}
			{#if product.hasPriceAnomaly}
				<span class="text-xs font-semibold px-2 py-1 rounded-full bg-amber-500/90 text-white backdrop-blur-sm shadow-sm flex items-center gap-1" data-testid="anomaly-badge">
					<TriangleAlert size={12} />
					Price Anomaly
				</span>
			{/if}
		</div>
		<!-- Store count + alert count + external link overlay -->
		<div class="absolute top-2 right-2 flex gap-1 z-10">
			{#if product.storeCount > 1}
				<span class="text-xs px-1.5 py-1 rounded-full bg-brand/90 text-white backdrop-blur-sm shadow-sm flex items-center gap-0.5" data-testid="store-count-badge">
					<Store size={10} />
					{product.storeCount}
				</span>
			{/if}
			{#if product.alertCount > 0}
				<span class="text-xs px-1.5 py-1 rounded-full bg-yellow-500/90 text-white backdrop-blur-sm shadow-sm flex items-center gap-0.5" data-testid="alert-count-badge">
					<Bell size={10} />
					{product.alertCount}
				</span>
			{/if}
			<a
				href={product.affiliateUrl ?? product.url}
				target="_blank"
				rel="noopener noreferrer external"
				class="p-1.5 rounded-full bg-white/80 dark:bg-gray-800/80 backdrop-blur-sm hover:bg-white dark:hover:bg-gray-800 text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 transition-colors shadow-sm"
				aria-label="Open product in new tab"
			>
				<ExternalLink size={14} />
			</a>
		</div>
	</div>

	<!-- Content area — fills remaining space -->
	<div class="flex flex-col flex-1 p-3 gap-2 relative">
		<!-- Product name — fixed 2-line height -->
		<div class="h-10">
			{#if isPending}
				<span class="font-medium text-gray-500 dark:text-gray-400 text-sm">Loading product data...</span>
			{:else}
				<a
					href={resolve(`/products/${product.id}`)}
					class="font-semibold text-gray-900 dark:text-white text-base hover:text-brand dark:hover:text-brand-muted line-clamp-2 leading-snug"
					title={product.name}
				>{product.name}</a>
			{/if}
		</div>

		<!-- Price row — fixed height -->
		<div class="flex items-center gap-2 h-7">
			{#if isPending}
				<span class="text-gray-500 dark:text-gray-400 text-sm flex items-center gap-2">
					<LoaderCircle size={14} class="animate-spin" />
					Fetching price...
				</span>
			{:else if product.currentPrice}
				<span class="text-lg font-bold text-gray-900 dark:text-white leading-none tabular-nums">
					{formatPrice(animatedPrice.current, product.currency)}
				</span>
				<ConvertedPrice amount={product.currentPrice} currency={product.currency} />
				{#if product.priceChange}
					<span class="relative inline-flex items-center justify-center">
						{#if product.priceChange <= -5}
							<span class="absolute inset-0 rounded-full animate-ping opacity-25 {getPriceChangeBgColor()}" style="animation-duration: 2s;"></span>
						{/if}
						<span class="relative inline-flex items-center gap-0.5 text-xs font-semibold px-1.5 py-0.5 rounded-full {getPriceChangeBgColor()}" data-testid="price-change-badge">
							{#if product.priceChange < 0}
								<TrendingDown size={12} />
							{:else}
								<TrendingUp size={12} />
							{/if}
							{Math.abs(product.priceChange).toFixed(1)}%
						</span>
					</span>
				{/if}
			{:else if product.isOutOfStock}
				<span class="text-red-600 dark:text-red-400 text-sm font-medium">Out of Stock</span>
			{:else}
				<span class="text-gray-500 dark:text-gray-400 text-sm">Price unavailable</span>
			{/if}
		</div>

		<!-- Sparkline — needs 3+ points; a 2-point series is just a meaningless diagonal stub. -->
		{#if product.sparkline && product.sparkline.length >= 3}
			<div class="h-5">
				<Sparkline data={product.sparkline} />
			</div>
		{/if}

		<!-- Price Story Label -->
		{#if priceStory}
			<div class="h-5 flex items-center">
				<span class="text-xs font-medium px-1.5 py-0.5 rounded-full {storyVariantClasses[priceStory.variant]}" data-testid="price-story">
					{priceStory.text}
				</span>
			</div>
		{/if}

		<!-- Tags row — single line, overflow hidden, fixed height -->
		<div class="h-6 flex items-center gap-1 overflow-hidden">
			{#if (product.tags ?? []).length > 0}
				{#each (product.tags ?? []) as tag (tag.id)}
					<TagBadge name={tag.name} color={tag.color} />
				{/each}
			{/if}
		</div>

		<!-- Footer — pinned to bottom -->
		<div class="mt-auto pt-2 border-t border-gray-100 dark:border-gray-700 flex items-center justify-between">
			<span
				class="text-xs text-gray-500 dark:text-gray-400"
				title={product.lastChecked ? new Date(product.lastChecked).toLocaleString() : undefined}
			>
				{#if isPending}
					Processing...
				{:else if product.lastChecked}
					Updated {formatTimeAgo(product.lastChecked)}
				{:else}
					Never checked
				{/if}
			</span>
			<div class="flex gap-0.5">
				{#if !isPending && onViewHistory}
					<button
						onclick={() => onViewHistory?.(product)}
						class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
						aria-label="View price history"
					>
						<Clock size={16} />
					</button>
				{/if}
				{#if canToggleStatus && onStatusChange}
					<button
						onclick={() =>
							onStatusChange?.(
								product.id,
								product.status === 'active' ? 'paused' : 'active'
							)}
						class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
						aria-label={product.status === 'active' ? 'Pause tracking' : 'Resume tracking'}
					>
						{#if product.status === 'active'}
							<Pause size={16} />
						{:else}
							<Play size={16} />
						{/if}
					</button>
				{/if}
				{#if !isPending}
					<button
						onclick={toggleQuickAlert}
						class="p-1.5 text-gray-400 hover:text-yellow-600 dark:hover:text-yellow-400 hover:bg-yellow-50 dark:hover:bg-yellow-900/30 rounded"
						aria-label="Set quick price alert"
					>
						<Bell size={16} />
					</button>
				{/if}
				{#if onDelete}
					<button
						onclick={() => onDelete?.(product.id)}
						class="p-1.5 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded"
						aria-label="Delete product"
					>
						<Trash2 size={16} />
					</button>
				{/if}
			</div>
		</div>

		<!-- Quick alert overlay — positioned over card content so it doesn't expand the card -->
		{#if quickAlert.open}
			<div class="absolute inset-x-0 bottom-0 bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700 px-3 py-2 z-20 rounded-b-lg" role="region" aria-label="Quick price alert for {product.name}">
				{#if quickAlert.success}
					<p class="text-sm text-green-600 dark:text-green-400 py-1">Alert set ✓</p>
				{:else}
					<div class="flex items-center gap-2">
						<label for="quick-alert-price-{product.id}" class="text-xs text-gray-500 dark:text-gray-400 whitespace-nowrap">Alert below</label>
						<div class="flex items-center gap-1 flex-1">
							<span class="text-xs text-gray-500 dark:text-gray-400">{product.currency}</span>
							<input
								id="quick-alert-price-{product.id}"
								type="number"
								step="0.01"
								min="0"
								bind:value={quickAlert.price}
								disabled={quickAlert.loading}
								class="w-24 px-2 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
							/>
						</div>
						<button
							onclick={submitQuickAlert}
							disabled={quickAlert.loading}
							class="p-1 text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30 rounded disabled:opacity-50"
							aria-label="Create alert"
						>
							<Check size={16} />
						</button>
						<button
							onclick={toggleQuickAlert}
							class="p-1 text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 rounded"
							aria-label="Cancel"
						>
							<X size={16} />
						</button>
					</div>
					{#if quickAlert.error}
						<FormError size="xs" class="mt-1" message={quickAlert.error}>
							{#if quickAlert.isNetworkError}
								<button
									onclick={submitQuickAlert}
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
		{/if}
	</div>
</div>
