<script lang="ts">
	import { BadgeDollarSign, Bell } from 'lucide-svelte';

	export type Opportunity = 'lowest' | 'near';

	interface Props {
		atLowestCount: number;
		nearTargetCount: number;
		active: Opportunity | null;
		onSelect: (opportunity: Opportunity | null) => void;
	}

	let { atLowestCount, nearTargetCount, active, onSelect }: Props = $props();

	// Counts only, never a total of prices: products are in different currencies. A chip at zero is
	// hidden unless it is the active filter, which must stay visible so it can be turned off.
	const chips = $derived(
		[
			{
				key: 'lowest' as const,
				count: atLowestCount,
				text: `${atLowestCount} at their 90-day low`,
				icon: BadgeDollarSign
			},
			{
				key: 'near' as const,
				count: nearTargetCount,
				text: `${nearTargetCount} ${nearTargetCount === 1 ? 'alert' : 'alerts'} near target`,
				icon: Bell
			}
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
				{chip.text}
			</button>
		{/each}
	</div>
{/if}
