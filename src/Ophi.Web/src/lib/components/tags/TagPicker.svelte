<script lang="ts">
	import { Plus, LoaderCircle } from 'lucide-svelte';
	import type { Tag, ProductTag } from '$lib/api/client';
	import TagBadge from './TagBadge.svelte';

	interface Props {
		availableTags: Tag[];
		selectedTags: ProductTag[];
		onAdd: (tagId: string) => Promise<void>;
		onRemove: (tagId: string) => Promise<void>;
	}

	let { availableTags, selectedTags, onAdd, onRemove }: Props = $props();

	let isOpen = $state(false);
	let loading = $state<string | null>(null);
	let focusedIndex = $state(-1);

	const unselectedTags = $derived(
		availableTags.filter((tag) => !selectedTags.some((st) => st.id === tag.id))
	);

	async function handleAddTag(tagId: string) {
		loading = tagId;
		try {
			await onAdd(tagId);
		} finally {
			loading = null;
		}
	}

	async function handleRemoveTag(tagId: string) {
		loading = tagId;
		try {
			await onRemove(tagId);
		} finally {
			loading = null;
		}
	}

	function handleClickOutside(e: MouseEvent) {
		const target = e.target as HTMLElement;
		if (!target.closest('[data-tag-picker]')) {
			isOpen = false;
			focusedIndex = -1;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (!isOpen || unselectedTags.length === 0) return;

		if (e.key === 'ArrowDown') {
			e.preventDefault();
			focusedIndex = (focusedIndex + 1) % unselectedTags.length;
		} else if (e.key === 'ArrowUp') {
			e.preventDefault();
			focusedIndex = (focusedIndex - 1 + unselectedTags.length) % unselectedTags.length;
		} else if (e.key === 'Enter' && focusedIndex >= 0) {
			e.preventDefault();
			const tag = unselectedTags[focusedIndex];
			if (tag && loading !== tag.id) {
				handleAddTag(tag.id);
			}
		} else if (e.key === 'Escape') {
			e.preventDefault();
			isOpen = false;
			focusedIndex = -1;
		}
	}

	function toggleDropdown() {
		isOpen = !isOpen;
		focusedIndex = isOpen ? 0 : -1;
	}
</script>

<svelte:window onclick={handleClickOutside} />

<div class="relative" data-tag-picker data-testid="tag-picker">
	<!-- Selected tags display -->
	<div class="flex flex-wrap gap-1.5 mb-2">
		{#each selectedTags as tag (tag.id)}
			<TagBadge
				name={tag.name}
				color={tag.color}
				removable
				onRemove={() => handleRemoveTag(tag.id)}
			/>
		{/each}
	</div>

	<!-- Add tag button -->
	<button
		type="button"
		onclick={toggleDropdown}
		onkeydown={handleKeydown}
		class="inline-flex items-center gap-1 px-2 py-1 text-xs text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white hover:bg-gray-100 dark:hover:bg-gray-700 rounded transition-colors"
		data-testid="add-tag-button"
	>
		<Plus size={14} />
		Add tag
	</button>

	<!-- Dropdown -->
	{#if isOpen}
		<div
			class="absolute z-10 mt-1 w-48 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg py-1 max-h-48 overflow-y-auto"
			data-testid="tag-dropdown"
			role="listbox"
			tabindex="0"
			onkeydown={handleKeydown}
		>
			{#if unselectedTags.length === 0}
				<div class="px-3 py-2 text-sm text-gray-500 dark:text-gray-400">No more tags available</div>
			{:else}
				{#each unselectedTags as tag, i (tag.id)}
					<button
						type="button"
						onclick={() => handleAddTag(tag.id)}
						disabled={loading === tag.id}
						class="w-full flex items-center gap-2 px-3 py-2 text-sm text-left hover:bg-gray-100 dark:hover:bg-gray-700 disabled:opacity-50 {focusedIndex === i ? 'bg-gray-100 dark:bg-gray-700' : ''}"
						role="option"
						aria-selected={focusedIndex === i}
					>
						<span
							class="w-3 h-3 rounded-full shrink-0"
							style="background-color: {tag.color}"
						></span>
						<span class="text-gray-700 dark:text-gray-300 truncate">{tag.name}</span>
						{#if loading === tag.id}
							<LoaderCircle size={14} class="ml-auto text-gray-400 animate-spin" />
						{/if}
					</button>
				{/each}
			{/if}
		</div>
	{/if}
</div>
