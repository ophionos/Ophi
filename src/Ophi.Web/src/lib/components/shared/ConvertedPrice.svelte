<script lang="ts">
	import { fx } from '$lib/stores/fx.svelte';
	import { convert, isStale } from '$lib/utils/fx';
	import { formatPrice } from '$lib/format';

	interface Props {
		amount: number | null | undefined;
		/** The price's own (native) currency. */
		currency: string;
		class?: string;
	}

	let { amount, currency, class: className = '' }: Props = $props();

	const converted = $derived(
		fx.displayCurrency && amount != null
			? convert(amount, currency, fx.displayCurrency, fx.rates)
			: null
	);

	const asOfLabel = $derived(
		fx.asOf
			? new Date(fx.asOf).toLocaleDateString('en-US', {
					month: 'short',
					day: 'numeric',
					timeZone: 'UTC'
				})
			: null
	);
	const stale = $derived(fx.asOf ? isStale(fx.asOf) : false);
</script>

{#if converted != null && fx.displayCurrency}
	<span
		class="text-xs font-normal text-gray-400 dark:text-gray-500 tabular-nums whitespace-nowrap {className}"
		title="Approximate — ECB reference rate{asOfLabel ? ` of ${asOfLabel}` : ''}"
		data-testid="converted-price"
	>
		≈ {formatPrice(converted, fx.displayCurrency)}{#if stale && asOfLabel}
			(rates from {asOfLabel}){/if}
	</span>
{/if}
