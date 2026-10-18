<script lang="ts" module>
	export interface ActiveFilter {
		key: string;
		label: string;
		onRemove: () => void;
	}

	export const SORT_OPTIONS = [
		{ value: '', label: 'Recently updated' },
		{ value: 'favourites:asc', label: 'Favourites first' },
		{ value: 'name:asc', label: 'Name A–Z' },
		{ value: 'name:desc', label: 'Name Z–A' },
		{ value: 'price:asc', label: 'Price, low to high' },
		{ value: 'price:desc', label: 'Price, high to low' },
		{ value: 'pricechange:asc', label: 'Biggest drop' },
		{ value: 'pricechange:desc', label: 'Biggest rise' },
		{ value: 'lastchecked:desc', label: 'Checked recently' },
		{ value: 'dateadded:desc', label: 'Newest added' },
		{ value: 'dateadded:asc', label: 'Oldest added' }
	];
</script>

<script lang="ts">
	import { Search, X, Star, ChevronDown } from 'lucide-svelte';
	import type { Snippet } from 'svelte';
	import type { ProductStatus, Tag } from '$lib/api/client';

	interface Props {
		search: string;
		status: ProductStatus | '';
		selectedTagId: string | null;
		storeId: string | null;
		sortBy: string;
		sortDirection: string;
		favouriteOnly: boolean;
		tags: Tag[];
		stores: { storeId: string; name: string }[];
		/** Every filter in effect, each removable; drives the chip row and "Clear all". */
		activeFilters: ActiveFilter[];
		onSearchChange: (value: string) => void;
		onStatusChange: (value: ProductStatus | '') => void;
		onTagChange: (tagId: string | null) => void;
		onStoreChange: (storeId: string | null) => void;
		onSortChange: (sortBy: string, sortDirection: string) => void;
		onFavouriteChange: (value: boolean) => void;
		onClearAll: () => void;
		/** Rendered at the end of the row (selection and view toggles). */
		trailing?: Snippet;
	}

	let {
		search,
		status,
		selectedTagId,
		storeId,
		sortBy,
		sortDirection,
		favouriteOnly,
		tags,
		stores,
		activeFilters,
		onSearchChange,
		onStatusChange,
		onTagChange,
		onStoreChange,
		onSortChange,
		onFavouriteChange,
		onClearAll,
		trailing
	}: Props = $props();

	const sortValue = $derived(sortBy ? `${sortBy}:${sortDirection}` : '');

	function handleSort(value: string) {
		const [by, direction] = value.split(':');
		onSortChange(by ?? '', direction ?? 'asc');
	}

	const selectClass =
		'min-h-11 w-full appearance-none rounded-lg border border-gray-300 bg-surface-1 py-2 pr-9 pl-3 text-sm text-gray-900 focus:border-transparent focus:ring-2 focus:ring-brand dark:border-gray-600 dark:text-white';
</script>

<div class="mb-4" data-testid="filter-bar" data-onboarding="filter-bar">
	<div class="flex flex-wrap items-center gap-2">
		<div class="relative min-w-48 flex-1 basis-full sm:basis-auto">
			<label for="product-search" class="sr-only">Filter products</label>
			<Search
				class="absolute top-1/2 left-3 -translate-y-1/2 text-gray-400"
				size={18}
				aria-hidden="true"
			/>
			<input
				id="product-search"
				type="text"
				value={search}
				oninput={(e) => onSearchChange(e.currentTarget.value)}
				placeholder="Filter by name or URL"
				class="min-h-11 w-full rounded-lg border border-gray-300 bg-surface-1 py-2 pr-10 pl-10 text-sm text-gray-900 placeholder-gray-400 focus:border-transparent focus:ring-2 focus:ring-brand dark:border-gray-600 dark:text-white dark:placeholder-gray-500"
				data-testid="search-input"
			/>
			{#if search}
				<button
					type="button"
					onclick={() => onSearchChange('')}
					class="absolute top-1/2 right-1 inline-flex min-h-11 min-w-11 -translate-y-1/2 items-center justify-center text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
					aria-label="Clear the text filter"
				>
					<X size={16} />
				</button>
			{/if}
		</div>

		{#snippet select(
			id: string,
			label: string,
			value: string,
			onchange: (value: string) => void,
			options: { value: string; label: string }[],
			testid: string
		)}
			<div class="relative">
				<label for={id} class="sr-only">{label}</label>
				<select
					{id}
					{value}
					onchange={(e) => onchange(e.currentTarget.value)}
					class={selectClass}
					data-testid={testid}
				>
					{#each options as option (option.value)}
						<option value={option.value}>{option.label}</option>
					{/each}
				</select>
				<ChevronDown
					size={16}
					class="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-gray-400"
					aria-hidden="true"
				/>
			</div>
		{/snippet}

		{@render select(
			'status-select',
			'Status',
			status,
			(value) => onStatusChange(value as ProductStatus | ''),
			[
				{ value: '', label: 'Any status' },
				{ value: 'active', label: 'Active' },
				{ value: 'paused', label: 'Paused' },
				{ value: 'error', label: 'Error' }
			],
			'status-select'
		)}
		{#if tags.length > 0}
			{@render select(
				'tag-select',
				'Tag',
				selectedTagId ?? '',
				(value) => onTagChange(value || null),
				[{ value: '', label: 'Any tag' }, ...tags.map((t) => ({ value: t.id, label: t.name }))],
				'tag-select'
			)}
		{/if}
		{#if stores.length > 1}
			{@render select(
				'store-select',
				'Store',
				storeId ?? '',
				(value) => onStoreChange(value || null),
				[
					{ value: '', label: 'Any store' },
					...stores.map((s) => ({ value: s.storeId, label: s.name }))
				],
				'store-select'
			)}
		{/if}
		{@render select('sort-select', 'Sort by', sortValue, handleSort, SORT_OPTIONS, 'sort-select')}

		<button
			type="button"
			onclick={() => onFavouriteChange(!favouriteOnly)}
			aria-pressed={favouriteOnly}
			class="inline-flex min-h-11 items-center gap-1.5 rounded-lg border px-3 text-sm transition-colors {favouriteOnly
				? 'border-brand bg-brand text-white'
				: 'border-gray-300 bg-surface-1 text-gray-700 hover:bg-surface-2 dark:border-gray-600 dark:text-gray-200'}"
			data-testid="favourite-filter"
		>
			<Star size={15} class={favouriteOnly ? 'fill-white' : ''} aria-hidden="true" />
			Favourites
		</button>

		{#if trailing}
			<div class="ml-auto flex items-center gap-2">
				{@render trailing()}
			</div>
		{/if}
	</div>

	{#if activeFilters.length > 0}
		<div class="mt-2 flex flex-wrap items-center gap-2" data-testid="active-filters">
			{#each activeFilters as filter (filter.key)}
				<span
					class="inline-flex items-center gap-1 rounded-full bg-surface-2 py-0.5 pr-0.5 pl-3 text-sm text-gray-700 dark:text-gray-200"
					data-testid="active-filter-{filter.key}"
				>
					{filter.label}
					<button
						type="button"
						onclick={filter.onRemove}
						class="relative inline-flex h-7 w-7 items-center justify-center rounded-full hover:bg-surface-3 after:absolute after:-inset-2 after:content-['']"
						aria-label="Remove filter: {filter.label}"
					>
						<X size={14} />
					</button>
				</span>
			{/each}
			<button
				type="button"
				onclick={onClearAll}
				class="min-h-11 px-2 text-sm text-brand hover:underline dark:text-brand-muted"
				data-testid="clear-all-filters"
			>
				Clear all
			</button>
		</div>
	{/if}
</div>
