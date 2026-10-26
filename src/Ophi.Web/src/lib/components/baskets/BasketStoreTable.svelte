<script lang="ts">
	import { resolve } from '$app/paths';
	import type { Basket, ShippingRate } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import ConvertedPrice from '$lib/components/shared/ConvertedPrice.svelte';
	import BasketShipping from './BasketShipping.svelte';

	interface Props {
		basket: Basket;
		shippingRates: ShippingRate[];
	}

	let { basket, shippingRates }: Props = $props();

	const itemCount = $derived(basket.items.length);
</script>

<!-- Wide tables scroll inside this container, never the page. -->
<div class="overflow-x-auto">
	<table class="w-full text-sm">
		<thead>
			<tr class="border-b border-gray-200 dark:border-gray-700">
				<th class="px-4 py-3 text-left font-medium text-gray-500 dark:text-gray-400">Store</th>
				{#each basket.items as item (item.productId)}
					<th class="px-4 py-3 text-left font-medium text-gray-900 dark:text-white">
						<a
							href={resolve(`/products/${item.productId}`)}
							class="block max-w-40 truncate hover:text-brand dark:hover:text-brand-muted"
							title={item.productName}>{item.productName}</a
						>
					</th>
				{/each}
				<th class="px-4 py-3 text-left font-medium text-gray-500 dark:text-gray-400">Shipping</th>
				<th class="px-4 py-3 text-left font-medium text-gray-500 dark:text-gray-400">Total</th>
			</tr>
		</thead>
		<tbody>
			{#each basket.stores as store (store.host)}
				{@const isBest = store.complete && store.host === basket.bestStore}
				<tr
					data-testid="basket-store-row"
					data-host={store.host}
					class="border-b border-gray-100 dark:border-gray-700 last:border-0 {isBest
						? 'bg-success/5 font-medium'
						: ''} {store.complete ? '' : 'text-gray-400 dark:text-gray-500'}"
				>
					<td class="px-4 py-3">
						<div class="flex items-center gap-2">
							<span class="text-gray-900 dark:text-white {store.complete ? '' : 'opacity-60'}"
								>{store.host}</span
							>
							{#if isBest}
								<span
									data-testid="best-badge"
									class="inline-flex shrink-0 items-center px-2 py-0.5 text-xs font-semibold bg-success/15 text-success rounded-full"
									>Best</span
								>
							{/if}
						</div>
					</td>
					{#each basket.items as item (item.productId)}
						{@const cell = store.items.find((i) => i.productId === item.productId)}
						<td class="px-4 py-3 tabular-nums">
							{#if cell && cell.price != null && cell.currency}
								{formatPrice(cell.price * item.quantity, cell.currency)}
							{:else}
								<span class="text-gray-400 dark:text-gray-500">not available</span>
							{/if}
						</td>
					{/each}
					<td class="px-4 py-3" data-testid="shipping-cell">
						<BasketShipping
							host={store.host}
							shipping={store.shipping}
							currency={store.currency}
							rates={shippingRates}
						/>
					</td>
					<td class="px-4 py-3" data-testid="total-cell">
						{#if !store.complete}
							<span>{itemCount - store.missingCount} of {itemCount} items</span>
						{:else if store.total != null && store.currency}
							<span class="tabular-nums text-gray-900 dark:text-white"
								>{formatPrice(store.total, store.currency)}</span
							>
							<div><ConvertedPrice amount={store.total} currency={store.currency} /></div>
						{:else}
							<span
								title={store.currency == null
									? 'Prices in different currencies'
									: 'Shipping in another currency'}>unknown</span
							>
						{/if}
					</td>
				</tr>
			{/each}
		</tbody>
	</table>
</div>
