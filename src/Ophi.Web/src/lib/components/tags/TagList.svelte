<script lang="ts">
	import { Pen, Trash2 } from 'lucide-svelte';
	import type { Tag } from '$lib/api/client';
	import TagBadge from './TagBadge.svelte';

	interface Props {
		tags: Tag[];
		onEdit: (tag: Tag) => void;
		onDelete: (tag: Tag) => void;
	}

	let { tags, onEdit, onDelete }: Props = $props();

	const sortedTags = $derived([...tags].sort((a, b) => b.weight - a.weight || a.name.localeCompare(b.name)));
</script>

<div class="space-y-2" data-testid="tag-list">
	{#each sortedTags as tag (tag.id)}
		<div
			class="flex items-center justify-between p-3 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg hover:shadow-sm transition-shadow"
			data-testid="tag-list-item"
		>
			<div class="flex items-center gap-3">
				<a
					href={`/dashboard?tag=${tag.id}`}
					title="View products with this tag"
					class="hover:opacity-80 transition-opacity"
				>
					<TagBadge name={tag.name} color={tag.color} />
				</a>
				<span class="text-sm text-gray-500 dark:text-gray-400">
					{tag.productCount} {tag.productCount === 1 ? 'product' : 'products'}
				</span>
			</div>

			<div class="flex items-center gap-1">
				<button
					onclick={() => onEdit(tag)}
					class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
					title="Edit tag"
					data-testid="tag-edit-button"
				>
					<Pen size={16} />
				</button>
				<button
					onclick={() => onDelete(tag)}
					class="p-1.5 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded"
					title="Delete tag"
					data-testid="tag-delete-button"
				>
					<Trash2 size={16} />
				</button>
			</div>
		</div>
	{:else}
		<div class="text-center py-8 text-gray-500 dark:text-gray-400">
			No tags created yet. Create your first tag to organize your products.
		</div>
	{/each}
</div>
