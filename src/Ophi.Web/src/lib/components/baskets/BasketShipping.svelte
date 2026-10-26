<script lang="ts">
	import type { ShippingRate } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import { findShippingRate } from '$lib/utils/shipping';

	interface Props {
		host: string;
		/** The server's shipping for the host: null = rate in another currency, or a store that mixes currencies (currency null); 0 = none or free. */
		shipping: number | null;
		currency: string | null;
		rates: ShippingRate[];
	}

	let { host, shipping, currency, rates }: Props = $props();

	const rate = $derived(findShippingRate(rates, host));
	const unit = $derived(currency ?? rate?.currency ?? '');
</script>

{#if shipping == null && currency == null}
	<span class="text-gray-400 dark:text-gray-500" title="Prices in different currencies">—</span>
{:else if shipping == null}
	<span class="text-warning">other currency</span>
{:else if shipping > 0}
	<span class="tabular-nums">{formatPrice(shipping, unit)}</span>
{:else if rate}
	<span class="text-gray-600 dark:text-gray-300">free</span>
{:else}
	<span class="text-gray-400 dark:text-gray-500" title="No shipping cost set">—</span>
{/if}
