<script lang="ts">
	import { BadgeDollarSign, Bell, TrendingDown, TriangleAlert } from 'lucide-svelte';

	export type Opportunity = 'drop' | 'near' | 'action' | 'lowest';

	interface Props {
		priceDropCount: number;
		nearTargetCount: number;
		needsActionCount: number;
		atLowestCount: number;
		active: Opportunity | null;
		onSelect: (opportunity: Opportunity | null) => void;
	}

	let { priceDropCount, nearTargetCount, needsActionCount, atLowestCount, active, onSelect }: Props = $props();

	// Counts only, never a total of prices: products are in different currencies. A chip at zero is
	// hidden unless it is the active filter, which must stay visible so it can be turned off.
	const chips = $derived(
		[
			{ key: 'drop' as const, count: priceDropCount, label: 'Price dropped (7 days)', icon: TrendingDown },
			{ key: 'near' as const, count: nearTargetCount, label: 'Near target', icon: Bell },
			{ key: 'action' as const, count: needsActionCount, label: 'Needs action', icon: TriangleAlert },
			{ key: 'lowest' as const, count: atLowestCount, label: 'At a real low', icon: BadgeDollarSign }
		].filter((chip) => chip.count > 0 || chip.key === active)
	);
</script>

{#if chips.length > 0}
	<div class="-mx-4 mb-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0" data-testid="opportunity-chips">
		{#each chips as chip (chip.key)}
			{@const Icon = chip.icon}
			<button
				type="button"
				onclick={() => onSelect(active === chip.key ? null : chip.key)}
				aria-pressed={active === chip.key}
				class="inline-flex min-h-11 shrink-0 items-center gap-2 rounded-full border px-4 text-sm whitespace-nowrap transition-colors {active ===
				chip.key
					? 'border-brand bg-brand text-white'
					: 'border-gray-200 bg-surface-1 text-gray-700 hover:border-brand/50 dark:border-gray-700 dark:text-gray-200'}"
				data-testid="opportunity-{chip.key}"
			>
				<Icon size={15} aria-hidden="true" />
				{chip.label}
				<span
					class="rounded-full px-2 py-0.5 text-xs tabular-nums {active === chip.key
						? 'bg-white/20 text-white'
						: 'bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-300'}"
				>
					{chip.count}
				</span>
			</button>
		{/each}
	</div>
{/if}
