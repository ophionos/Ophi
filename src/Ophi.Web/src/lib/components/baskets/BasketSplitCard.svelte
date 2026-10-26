<script lang="ts">
	import type { Basket, ShippingRate } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import BasketShipping from './BasketShipping.svelte';

	interface Props {
		basket: Basket;
		shippingRates: ShippingRate[];
	}

	let { basket, shippingRates }: Props = $props();

	const split = $derived(basket.split);
	const best = $derived(basket.stores.find((s) => s.host === basket.bestStore && s.complete));

	// Cents, so float noise (28.1 - 30) does not leave a 1.9000000000000004 difference.
	const diffCents = $derived.by(() => {
		if (!split || !split.complete || !best) return null;
		if (split.total == null || best.total == null || best.currency !== split.currency) return null;
		return Math.round(best.total * 100) - Math.round(split.total * 100);
	});

	function item(productId: string) {
		return basket.items.find((i) => i.productId === productId);
	}
</script>

{#if split && split.lines.length > 0}
	<div data-testid="basket-split" class="space-y-3 p-4">
		<ul class="divide-y divide-gray-100 dark:divide-gray-700 text-sm">
			{#each split.lines as line (line.productId)}
				{@const it = item(line.productId)}
				<li data-testid="split-line" class="flex flex-wrap justify-between gap-2 py-2">
					<span class="text-gray-900 dark:text-white">{it?.productName ?? line.productId}</span>
					{#if line.host && line.price != null}
						<span class="tabular-nums text-gray-600 dark:text-gray-300">
							{line.host} · {formatPrice(line.price * (it?.quantity ?? 1), split.currency)}
						</span>
					{:else}
						<span class="text-gray-400 dark:text-gray-500">not available in {split.currency}</span>
					{/if}
				</li>
			{/each}
		</ul>

		{#if split.shipping.length > 0}
			<ul class="text-sm text-gray-600 dark:text-gray-300">
				{#each split.shipping as s (s.host)}
					<li data-testid="split-shipping" class="flex justify-between gap-2">
						<span>Shipping at {s.host}</span>
						<BasketShipping
							host={s.host}
							shipping={s.shipping}
							currency={split.currency}
							rates={shippingRates}
						/>
					</li>
				{/each}
			</ul>
		{/if}

		{#if split.total != null}
			<p class="flex justify-between gap-2 font-semibold text-gray-900 dark:text-white">
				<span>{split.complete ? 'Split total' : 'Total of available items'}</span>
				<span data-testid="split-total" class="tabular-nums"
					>{formatPrice(split.total, split.currency)}</span
				>
			</p>
		{/if}

		{#if !split.complete}
			<p data-testid="split-incomplete" class="text-sm text-warning">
				Some items are not available, so the split is incomplete and is not compared with the best
				store.
			</p>
		{:else if diffCents != null && best}
			<p data-testid="split-difference" class="text-sm text-gray-600 dark:text-gray-300">
				{#if diffCents > 0}
					{formatPrice(diffCents / 100, split.currency)} cheaper than buying everything at {best.host}
				{:else if diffCents < 0}
					{formatPrice(-diffCents / 100, split.currency)} more than buying everything at {best.host}
					(extra shipping)
				{:else}
					Same price as buying everything at {best.host}
				{/if}
			</p>
		{/if}
	</div>
{/if}
