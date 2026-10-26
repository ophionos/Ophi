<script lang="ts">
	import { density, type Density } from '$lib/stores/density.svelte';

	const OPTIONS: { value: Density; label: string }[] = [
		{ value: 'comfortable', label: 'Comfortable' },
		{ value: 'compact', label: 'Compact' }
	];
</script>

<div
	class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-2"
>
	<div class="flex items-center justify-between gap-3">
		<div class="min-w-0">
			<p id="density-label" class="text-sm font-medium text-gray-900 dark:text-white">Table density</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">The watchlist table on this device.</p>
		</div>
		<div role="radiogroup" aria-labelledby="density-label" class="flex shrink-0 gap-3">
			{#each OPTIONS as option (option.value)}
				<label class="flex min-h-11 items-center gap-2 text-sm text-gray-900 dark:text-white">
					<input
						type="radio"
						name="density"
						value={option.value}
						checked={density.current === option.value}
						onchange={() => density.set(option.value)}
						class="text-brand focus:ring-brand"
					/>
					{option.label}
				</label>
			{/each}
		</div>
	</div>
</div>
