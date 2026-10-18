<script lang="ts">
	import { formatPrice } from '$lib/format';

	interface Props {
		min: number;
		max: number;
		current: number;
		currency: string;
		/** How many days the range covers, for the label. */
		days?: number;
	}

	let { min, max, current, currency, days = 90 }: Props = $props();

	const position = $derived(Math.min(100, Math.max(0, ((current - min) / (max - min)) * 100)));
	const label = $derived(
		`${days}-day range ${formatPrice(min, currency)} to ${formatPrice(max, currency)}, now ${formatPrice(current, currency)}`
	);
</script>

{#if min < max}
	<!-- The marker's place on a neutral track says where the price sits; the end labels give the
	     numbers. No colour scale: low is not "good" for every reader. -->
	<div class="w-28" role="img" aria-label={label} data-testid="price-range-bar">
		<div class="relative h-1.5 rounded-full bg-surface-3">
			<div
				class="absolute top-1/2 h-3 w-1.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-brand ring-2 ring-surface-1"
				style="left: {position}%"
				data-testid="range-marker"
			></div>
		</div>
		<div class="mt-1 flex justify-between text-[11px] leading-none text-gray-500 tabular-nums dark:text-gray-400" aria-hidden="true">
			<span>{formatPrice(min, currency)}</span>
			<span>{formatPrice(max, currency)}</span>
		</div>
	</div>
{/if}
