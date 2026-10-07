<script lang="ts">
	import { Rows3, Rows4 } from 'lucide-svelte';
	import { density, type Density } from '$lib/stores/density.svelte';

	function select(value: Density) {
		density.set(value);
	}
</script>

<div
	class="flex items-center gap-1 bg-gray-100 dark:bg-gray-700 rounded-lg p-0.5"
	role="group"
	aria-label="Row density"
	data-testid="density-toggle"
>
	<button
		onclick={() => select('comfortable')}
		class="p-1.5 rounded-md transition-colors {density.current === 'comfortable'
			? 'bg-white dark:bg-gray-600 text-brand dark:text-brand-muted shadow-sm'
			: 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'}"
		aria-label="Comfortable density"
		aria-pressed={density.current === 'comfortable'}
		data-testid="density-toggle-comfortable"
	>
		<Rows3 size={16} />
	</button>
	<button
		onclick={() => select('compact')}
		class="p-1.5 rounded-md transition-colors {density.current === 'compact'
			? 'bg-white dark:bg-gray-600 text-brand dark:text-brand-muted shadow-sm'
			: 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'}"
		aria-label="Compact density"
		aria-pressed={density.current === 'compact'}
		data-testid="density-toggle-compact"
	>
		<Rows4 size={16} />
	</button>
</div>
