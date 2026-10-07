<script lang="ts">
	import { resolve } from '$app/paths';
	import type { ComparisonGroup } from '$lib/api/client';

	interface Props {
		groups: ComparisonGroup[];
		/** The group this product belongs to, if any. */
		currentGroupId?: string;
		onAdd: (groupId: string) => void;
		onRemove: () => void;
	}

	let { groups, currentGroupId, onAdd, onRemove }: Props = $props();

	const currentGroup = $derived(
		currentGroupId ? (groups.find((g) => g.id === currentGroupId) ?? null) : null
	);
	const availableGroups = $derived(groups.filter((g) => g.id !== currentGroupId));
</script>

{#if currentGroup}
	<div class="flex items-center justify-between">
		<div>
			<p class="font-medium text-gray-900 dark:text-white">{currentGroup.name}</p>
			<p class="text-sm text-gray-500 dark:text-gray-400">
				{currentGroup.productCount} products in group
			</p>
		</div>
		<div class="flex gap-2">
			<a
				href={resolve(`/comparisons/${currentGroup.id}`)}
				class="px-3 py-1.5 text-sm font-medium text-brand dark:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded-md"
			>
				View Group
			</a>
			<button
				onclick={onRemove}
				class="px-3 py-1.5 text-sm font-medium text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-md"
			>
				Remove
			</button>
		</div>
	</div>
{:else if availableGroups.length > 0}
	<div class="flex items-center gap-3">
		<label for="comparison-group-select" class="text-sm text-gray-500 dark:text-gray-400">
			Add to comparison group:
		</label>
		<select
			id="comparison-group-select"
			onchange={(e) => onAdd(e.currentTarget.value)}
			class="px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
		>
			<option value="">Select a group...</option>
			{#each availableGroups as group (group.id)}
				<option value={group.id}>{group.name}</option>
			{/each}
		</select>
	</div>
{:else}
	<p class="text-sm text-gray-500 dark:text-gray-400">
		No comparison groups available. <a
			href={resolve('/comparisons')}
			class="text-brand dark:text-brand-muted hover:underline">Create one</a
		>
	</p>
{/if}
