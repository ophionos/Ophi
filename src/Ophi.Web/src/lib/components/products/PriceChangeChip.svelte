<script lang="ts">
	import { ArrowDown, ArrowUp } from 'lucide-svelte';

	interface Props {
		/** Percent change since the previous price; null or 0 when there is none. */
		change: number | null | undefined;
	}

	let { change }: Props = $props();

	// A drop is filled and a rise is light, and each has its own arrow: the chip does not rely on
	// hue alone.
	const isDrop = $derived(!!change && change < 0);
	const label = $derived(change ? `${isDrop ? 'Down' : 'Up'} ${Math.abs(change).toFixed(1)}%` : 'No change');
</script>

{#if change}
	<span
		class="inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-xs font-medium tabular-nums {isDrop
			? 'bg-price-drop text-surface-1'
			: 'bg-price-increase-subtle text-price-increase'}"
		aria-label={label}
		data-testid="price-change-chip"
	>
		{#if isDrop}
			<ArrowDown size={12} aria-hidden="true" />
		{:else}
			<ArrowUp size={12} aria-hidden="true" />
		{/if}
		{Math.abs(change).toFixed(1)}%
	</span>
{:else}
	<span class="text-xs text-price-unchanged" aria-label={label} data-testid="price-change-none">—</span>
{/if}
