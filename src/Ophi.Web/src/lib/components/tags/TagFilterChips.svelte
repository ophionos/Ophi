<script lang="ts">
	import type { Tag } from '$lib/api/client';

	interface Props {
		tags: Tag[];
		selectedTagId: string | null;
		onSelectTag: (tagId: string | null) => void;
		inline?: boolean;
	}

	let { tags, selectedTagId, onSelectTag, inline = false }: Props = $props();

	const sortedTags = $derived([...tags].sort((a, b) => b.weight - a.weight || a.name.localeCompare(b.name)));

	function getContrastColor(hexColor: string): string {
		const hex = hexColor.replace('#', '');
		const r = parseInt(hex.substring(0, 2), 16);
		const g = parseInt(hex.substring(2, 4), 16);
		const b = parseInt(hex.substring(4, 6), 16);
		const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
		return luminance > 0.5 ? '#000000' : '#ffffff';
	}
</script>

{#if inline}
	{#if selectedTagId !== null}
		<button
			onclick={() => onSelectTag(null)}
			class="px-3 py-1 rounded-full text-sm font-medium transition-colors bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600"
			data-testid="tag-filter-all"
		>
			All Tags
		</button>
	{/if}
	{#each sortedTags as tag (tag.id)}
		<button
			onclick={() => onSelectTag(tag.id)}
			class="px-3 py-1 rounded-full text-sm font-medium transition-all {selectedTagId === tag.id
				? 'ring-2 ring-offset-2 ring-brand dark:ring-offset-gray-900'
				: 'opacity-80 hover:opacity-100'}"
			style="background-color: {tag.color}; color: {getContrastColor(tag.color)}"
			data-testid="tag-filter-chip"
		>
			{tag.name}
		</button>
	{/each}
{:else}
	<div class="flex flex-wrap gap-2" data-testid="tag-filter-chips">
		<button
			onclick={() => onSelectTag(null)}
			class="px-3 py-1 rounded-full text-sm font-medium transition-colors {selectedTagId === null
				? 'bg-brand text-white'
				: 'bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}"
			data-testid="tag-filter-all"
		>
			All
		</button>

		{#each sortedTags as tag (tag.id)}
			<button
				onclick={() => onSelectTag(tag.id)}
				class="px-3 py-1 rounded-full text-sm font-medium transition-all {selectedTagId === tag.id
					? 'ring-2 ring-offset-2 ring-brand dark:ring-offset-gray-900'
					: 'opacity-80 hover:opacity-100'}"
				style="background-color: {tag.color}; color: {getContrastColor(tag.color)}"
				data-testid="tag-filter-chip"
			>
				{tag.name}
			</button>
		{/each}
	</div>
{/if}
