<script lang="ts">
	import { resolve } from '$app/paths';
	import type { Product } from '$lib/api/client';
	import { formatTimeAgo, formatPrice } from '$lib/format';
	import DealScore from '$lib/components/products/DealScore.svelte';
	import { TrendingDown, TrendingUp, Bell, ArrowRight, PackageX } from 'lucide-svelte';

	interface Props {
		products: Product[];
	}

	let { products }: Props = $props();

	interface FeedEvent {
		product: Product;
		type: 'drop' | 'rise' | 'out-of-stock';
		amount: number;
		percent: number;
	}

	const events = $derived.by(() => {
		const items: FeedEvent[] = [];
		for (const p of products) {
			if (p.isOutOfStock) {
				items.push({
					product: p,
					type: 'out-of-stock',
					amount: 0,
					percent: 0
				});
			} else if (p.priceChange != null && p.priceChange !== 0 && p.previousPrice != null && p.currentPrice != null) {
				const amount = Math.abs(p.currentPrice - p.previousPrice);
				items.push({
					product: p,
					type: p.priceChange < 0 ? 'drop' : 'rise',
					amount,
					percent: Math.abs(p.priceChange)
				});
			}
		}
		// Sort by lastChecked descending
		items.sort((a, b) => {
			const dateA = a.product.lastChecked ? new Date(a.product.lastChecked).getTime() : 0;
			const dateB = b.product.lastChecked ? new Date(b.product.lastChecked).getTime() : 0;
			return dateB - dateA;
		});
		return items;
	});
</script>

{#if events.length === 0}
	<div class="text-center py-12 bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
		<p class="text-gray-500 dark:text-gray-400">No recent activity</p>
		<p class="text-sm text-gray-400 dark:text-gray-500 mt-1">Price changes will appear here as they happen</p>
	</div>
{:else}
	<div class="space-y-2">
		{#each events as event (event.product.id)}
			<a
				href={resolve(`/products/${event.product.id}`)}
				class="flex items-center gap-4 p-4 bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700/50 hover:shadow-md hover:border-gray-300 dark:hover:border-gray-600 transition-all duration-200 group"
			>
				<!-- Event icon -->
				<div class="shrink-0 w-10 h-10 rounded-full flex items-center justify-center {event.type === 'drop' ? 'bg-green-100 dark:bg-green-900/40' : event.type === 'out-of-stock' ? 'bg-red-100 dark:bg-red-900/40' : 'bg-red-100 dark:bg-red-900/40'}">
					{#if event.type === 'drop'}
						<TrendingDown size={18} class="text-green-600 dark:text-green-400" />
					{:else if event.type === 'out-of-stock'}
						<PackageX size={18} class="text-red-600 dark:text-red-400" />
					{:else}
						<TrendingUp size={18} class="text-red-600 dark:text-red-400" />
					{/if}
				</div>

				<!-- Event details -->
				<div class="flex-1 min-w-0">
					<div class="flex items-center gap-2">
						<span class="font-medium text-gray-900 dark:text-white truncate" data-testid="event-product-name">
							{event.product.name}
						</span>
						{#if event.product.dealScore != null}
							<DealScore score={event.product.dealScore} />
						{/if}
					</div>
					<p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
						{#if event.type === 'out-of-stock'}
							<span class="font-medium text-red-600 dark:text-red-400">Out of stock</span>
							{#if event.product.currentPrice}
								<span class="text-gray-400 dark:text-gray-500"> · last known {formatPrice(event.product.currentPrice, event.product.currency)}</span>
							{/if}
						{:else}
							Price {event.type === 'drop' ? 'dropped' : 'increased'} by
							<span class="font-medium {event.type === 'drop' ? 'text-green-600 dark:text-green-400' : 'text-red-600 dark:text-red-400'}">
								{formatPrice(event.amount, event.product.currency)}
							</span>
							<span class="text-gray-400 dark:text-gray-500">({event.percent.toFixed(1)}%)</span>
						{/if}
						{#if event.product.alertCount > 0}
							<span class="inline-flex items-center gap-1 ml-2 text-xs text-amber-600 dark:text-amber-400">
								<Bell size={10} />
								{event.product.alertCount} alerts
							</span>
						{/if}
					</p>
				</div>

				<!-- Price + time -->
				<div class="shrink-0 text-right">
					<p class="font-semibold {event.type === 'out-of-stock' ? 'text-red-600 dark:text-red-400' : 'text-gray-900 dark:text-white'}">
						{#if event.type === 'out-of-stock'}
							OOS
						{:else}
							{event.product.currentPrice != null ? formatPrice(event.product.currentPrice, event.product.currency) : '—'}
						{/if}
					</p>
					<p class="text-xs text-gray-400 dark:text-gray-500 mt-0.5">
						{event.product.lastChecked ? formatTimeAgo(event.product.lastChecked) : 'Unknown'}
					</p>
				</div>

				<!-- Arrow -->
				<ArrowRight size={16} class="shrink-0 text-gray-300 dark:text-gray-600 group-hover:text-brand dark:group-hover:text-brand-muted transition-colors" />
			</a>
		{/each}
	</div>
{/if}
