<script lang="ts">
	import { resolve } from '$app/paths';
	import type { Product } from '$lib/api/client';
	import { formatTimeAgo, formatPrice } from '$lib/format';
	import { TrendingDown, TrendingUp, Bell, PackageX } from 'lucide-svelte';

	interface Props {
		products: Product[];
		/** The rail shows the latest few; the full history is on each product page. */
		limit?: number;
	}

	let { products, limit = 8 }: Props = $props();

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
		return items.slice(0, limit);
	});
</script>

<!-- A side rail next to the table or gallery; on a phone it stacks under them. -->
<section class="rounded-lg border border-gray-200 bg-surface-1 dark:border-gray-700" aria-labelledby="activity-title" data-testid="activity-rail">
	<h2 id="activity-title" class="border-b border-gray-100 px-4 py-3 text-sm font-semibold text-gray-900 dark:border-gray-700 dark:text-white">
		Recent activity
	</h2>
{#if events.length === 0}
	<div class="px-4 py-8 text-center">
		<p class="text-sm text-gray-500 dark:text-gray-400">No recent activity</p>
		<p class="text-xs text-gray-400 dark:text-gray-500 mt-1">Price changes will appear here as they happen</p>
	</div>
{:else}
	<ul class="divide-y divide-gray-100 dark:divide-gray-700">
		{#each events as event (event.product.id)}
			<li>
			<a
				href={resolve(`/products/${event.product.id}`)}
				class="flex min-h-11 items-center gap-3 px-4 py-2.5 hover:bg-surface-2 transition-colors"
			>
				<!-- Event icon -->
				<div class="shrink-0 w-8 h-8 rounded-full flex items-center justify-center {event.type === 'drop' ? 'bg-price-drop-subtle' : 'bg-price-increase-subtle'}">
					{#if event.type === 'drop'}
						<TrendingDown size={15} class="text-price-drop" />
					{:else if event.type === 'out-of-stock'}
						<PackageX size={15} class="text-danger" />
					{:else}
						<TrendingUp size={15} class="text-price-increase" />
					{/if}
				</div>

				<!-- Event details -->
				<div class="flex-1 min-w-0">
					<span class="block text-sm font-medium text-gray-900 dark:text-white truncate" data-testid="event-product-name">
						{event.product.name}
					</span>
					<p class="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
						{#if event.type === 'out-of-stock'}
							<span class="font-medium text-danger">Out of stock</span>
							{#if event.product.currentPrice}
								<span class="text-gray-400 dark:text-gray-500"> · last known {formatPrice(event.product.currentPrice, event.product.currency)}</span>
							{/if}
						{:else}
							Price {event.type === 'drop' ? 'dropped' : 'increased'} by
							<span class="font-medium {event.type === 'drop' ? 'text-price-drop' : 'text-price-increase'}">
								{formatPrice(event.amount, event.product.currency)}
							</span>
							<span class="text-gray-400 dark:text-gray-500">({event.percent.toFixed(1)}%)</span>
						{/if}
						{#if event.product.alertCount > 0}
							<span class="inline-flex items-center gap-1 ml-1 text-warning">
								<Bell size={10} />
								{event.product.alertCount} {event.product.alertCount === 1 ? 'alert' : 'alerts'}
							</span>
						{/if}
					</p>
				</div>

				<!-- Price + time -->
				<div class="shrink-0 text-right">
					<p class="text-sm font-semibold tabular-nums {event.type === 'out-of-stock' ? 'text-danger' : 'text-gray-900 dark:text-white'}">
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
			</a>
			</li>
		{/each}
	</ul>
{/if}
</section>
