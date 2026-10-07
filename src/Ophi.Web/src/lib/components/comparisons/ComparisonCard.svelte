<script lang="ts">
	import { Eye, Trash2, GitCompare } from 'lucide-svelte';
	import type { ComparisonGroup } from '$lib/api/client';

	interface Props {
		group: ComparisonGroup;
		onView?: (group: ComparisonGroup) => void;
		onDelete?: (group: ComparisonGroup) => void;
	}

	let { group, onView, onDelete }: Props = $props();
</script>

<div
	class="bg-white dark:bg-gray-800 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 p-4 hover:shadow-md transition-shadow"
>
	<div class="flex items-start justify-between">
		<div class="flex-1">
			<div class="flex items-center gap-2">
				<GitCompare size={18} class="text-brand dark:text-brand-muted" />
				<h3 class="font-medium text-gray-900 dark:text-white">{group.name}</h3>
			</div>
			<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
				{group.productCount}
				{group.productCount === 1 ? 'product' : 'products'}
			</p>
		</div>

		<div class="flex gap-1">
			{#if onView}
				<button
					onclick={() => onView?.(group)}
					class="p-2 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
					title="View"
				>
					<Eye size={16} />
				</button>
			{/if}
			{#if onDelete}
				<button
					onclick={() => onDelete?.(group)}
					class="p-2 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded"
					title="Delete"
				>
					<Trash2 size={16} />
				</button>
			{/if}
		</div>
	</div>
</div>
