<script lang="ts">
	import { TriangleAlert } from 'lucide-svelte';

	interface Props {
		currencies: string[];
	}

	let { currencies }: Props = $props();
</script>

{#if currencies.length > 1}
	<div
		data-testid="currency-mismatch-warning"
		class="flex items-center gap-2 p-3 mb-3 rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 text-amber-800 dark:text-amber-300 text-sm"
	>
		<TriangleAlert size={18} class="shrink-0" />
		<span>
			Prices are shown in different currencies ({currencies.join(', ')}). Direct comparison may
			not be accurate.
		</span>
	</div>
{/if}
