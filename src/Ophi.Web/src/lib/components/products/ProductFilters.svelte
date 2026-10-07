<script lang="ts">
	import { Search, ArrowUpDown, X, Star } from 'lucide-svelte';
	import type { Snippet } from 'svelte';
	import type { Tag } from '$lib/api/client';
	import TagFilterChips from '$lib/components/tags/TagFilterChips.svelte';

	interface Props {
		search: string;
		status: string;
		sortBy: string;
		sortDirection: string;
		favouriteOnly?: boolean;
		tags?: Tag[];
		selectedTagId?: string | null;
		onSearchChange: (value: string) => void;
		onStatusChange: (value: string) => void;
		onSortChange: (sortBy: string, sortDirection: string) => void;
		onFavouriteChange?: (value: boolean) => void;
		onTagChange?: (tagId: string | null) => void;
		/** Rendered at the start of the search/sort row (e.g. a section heading). */
		leading?: Snippet;
		/** Rendered at the end of the search/sort row (e.g. a view-mode toggle). */
		trailing?: Snippet;
	}

	let { search, status, sortBy, sortDirection, favouriteOnly = false, tags = [], selectedTagId = null, onSearchChange, onStatusChange, onSortChange, onFavouriteChange, onTagChange, leading, trailing }: Props = $props();

	const statusOptions = [
		{ value: '', label: 'All' },
		{ value: 'active', label: 'Active' },
		{ value: 'paused', label: 'Paused' },
		{ value: 'error', label: 'Error' }
	];

	const sortOptions = [
		{ value: '', label: 'Recently Updated' },
		{ value: 'name', label: 'Name' },
		{ value: 'price', label: 'Price' },
		{ value: 'dateadded', label: 'Date Added' },
		{ value: 'pricechange', label: 'Price Change' }
	];

	function handleSortChange(e: Event) {
		const target = e.target as HTMLSelectElement;
		const [newSortBy, newDirection] = target.value.split(':');
		onSortChange(newSortBy || '', newDirection || 'asc');
	}

	function clearSearch() {
		onSearchChange('');
	}

	const currentSortValue = $derived(
		sortBy ? `${sortBy}:${sortDirection}` : ''
	);
</script>

<div class="flex flex-col sm:flex-row sm:items-center gap-3 mb-3">
	{#if leading}
		{@render leading()}
	{/if}
	<!-- Search Input -->
	<div class="relative flex-1">
		<label for="product-search" class="sr-only">Search products</label>
		<Search class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" size={18} />
		<input
			id="product-search"
			type="text"
			value={search}
			oninput={(e) => onSearchChange(e.currentTarget.value)}
			placeholder="Search products..."
			class="w-full pl-10 pr-10 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent"
			data-testid="search-input"
		/>
		{#if search}
			<button
				onclick={clearSearch}
				class="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
				aria-label="Clear search"
			>
				<X size={18} />
			</button>
		{/if}
	</div>

	<!-- Sort Dropdown -->
	<div class="relative">
		<label for="sort-select" class="sr-only">Sort by</label>
		<ArrowUpDown class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" size={18} />
		<select
			id="sort-select"
			value={currentSortValue}
			onchange={handleSortChange}
			class="pl-10 pr-8 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent appearance-none cursor-pointer min-w-45"
			data-testid="sort-select"
		>
			{#each sortOptions as opt (opt.value)}
				<option value={opt.value ? `${opt.value}:asc` : ''}>{opt.label}</option>
				{#if opt.value}
					<option value={`${opt.value}:desc`}>{opt.label} (desc)</option>
				{/if}
			{/each}
		</select>
	</div>
	{#if trailing}
		{@render trailing()}
	{/if}
</div>

<!-- Combined filter row: status chips + separator + tag chips -->
<div class="flex flex-wrap items-center gap-2 mb-3" data-testid="status-filters">
	{#each statusOptions as opt (opt.value)}
		<button
			onclick={() => onStatusChange(opt.value)}
			class="px-3 py-1 rounded-full text-sm font-medium transition-colors {status === opt.value
				? 'bg-brand text-white'
				: 'bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}"
			data-testid="status-chip-{opt.value || 'all'}"
		>
			{opt.label}
		</button>
	{/each}
	{#if onFavouriteChange}
		<button
			onclick={() => onFavouriteChange?.(!favouriteOnly)}
			class="px-3 py-1 rounded-full text-sm font-medium transition-colors flex items-center gap-1.5 {favouriteOnly
				? 'bg-yellow-500 text-white'
				: 'bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}"
			data-testid="favourite-filter"
		>
			<Star size={14} class={favouriteOnly ? 'fill-white' : ''} />
			Favourites
		</button>
	{/if}
	{#if tags.length > 0 && onTagChange}
		<div class="w-px h-5 bg-gray-300 dark:bg-gray-600 mx-1"></div>
		<TagFilterChips {tags} {selectedTagId} onSelectTag={onTagChange} inline />
	{/if}
</div>
