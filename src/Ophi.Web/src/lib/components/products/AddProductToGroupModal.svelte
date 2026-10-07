<script lang="ts">
	import { untrack } from 'svelte';
	import { SvelteMap } from 'svelte/reactivity';
	import { LoaderCircle, Package, Check, Search, X } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import DashboardPagination from '$lib/components/products/DashboardPagination.svelte';
	import { api, type Product } from '$lib/api/client';
	import { formatPrice } from '$lib/format';

	interface Props {
		isOpen: boolean;
		existingProductIds: string[];
		onClose: () => void;
		onAdd: (productIds: string[]) => Promise<void>;
	}

	let { isOpen, existingProductIds, onClose, onAdd }: Props = $props();

	const PAGE_SIZE = 10;
	const SEARCH_DEBOUNCE_MS = 300;

	let searchQuery = $state('');
	let searchInput = $state('');
	let page = $state(1);
	let results = $state<Product[]>([]);
	let total = $state(0);
	let loading = $state(false);
	let submitting = $state(false);

	// Stores the full Product objects so selections survive pagination/search — a selected item
	// that's no longer in the current page/filter still renders in the sticky "Selected" section.
	const selected = new SvelteMap<string, Product>();

	let inFlight: AbortController | null = null;
	let searchTimer: ReturnType<typeof setTimeout> | null = null;
	let wasOpen = false;

	const totalPages = $derived(Math.max(1, Math.ceil(total / PAGE_SIZE)));

	// Reset and load a fresh first page each time the modal opens. untrack keeps the loadProducts()
	// reads of page/searchQuery from becoming dependencies of this effect (which would otherwise
	// re-fire — and wipe selections — on every page change).
	$effect(() => {
		if (isOpen && !wasOpen) {
			wasOpen = true;
			untrack(() => {
				searchQuery = '';
				searchInput = '';
				page = 1;
				selected.clear();
				loadProducts();
			});
		} else if (!isOpen) {
			wasOpen = false;
		}
	});

	async function loadProducts() {
		inFlight?.abort();
		const controller = new AbortController();
		inFlight = controller;
		loading = true;
		try {
			const res = await api.getProducts({
				search: searchQuery || undefined,
				page,
				pageSize: PAGE_SIZE
			});
			if (controller.signal.aborted) return;
			results = res.items;
			total = res.total;
		} catch {
			if (controller.signal.aborted) return;
			results = [];
			total = 0;
		} finally {
			if (inFlight === controller) {
				inFlight = null;
				loading = false;
			}
		}
	}

	function handleSearchInput(event: Event) {
		searchInput = (event.currentTarget as HTMLInputElement).value;
		if (searchTimer) clearTimeout(searchTimer);
		searchTimer = setTimeout(() => {
			searchQuery = searchInput;
			page = 1;
			loadProducts();
		}, SEARCH_DEBOUNCE_MS);
	}

	function handlePageChange(p: number) {
		page = p;
		loadProducts();
	}

	function isInGroup(productId: string): boolean {
		return existingProductIds.includes(productId);
	}

	function toggleSelect(product: Product) {
		if (isInGroup(product.id)) return;
		if (selected.has(product.id)) {
			selected.delete(product.id);
		} else {
			selected.set(product.id, product);
		}
	}

	async function handleAdd() {
		if (selected.size === 0) return;
		submitting = true;
		try {
			await onAdd([...selected.keys()]);
			selected.clear();
		} finally {
			submitting = false;
		}
	}

	function handleClose() {
		if (submitting) return;
		onClose();
	}
</script>

<Modal
	{isOpen}
	title="Add Products"
	size="md"
	closeOnBackdrop={!submitting}
	closeOnEscape={!submitting}
	closeDisabled={submitting}
	onClose={handleClose}
>
	<div class="px-4 pt-4">
		<div class="relative">
			<Search
				size={16}
				class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 pointer-events-none"
			/>
			<input
				type="search"
				value={searchInput}
				oninput={handleSearchInput}
				placeholder="Search products…"
				aria-label="Search products"
				class="w-full pl-9 pr-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand"
			/>
		</div>
	</div>

	{#if selected.size > 0}
		<div
			class="mx-4 mt-3 rounded-md border border-brand/40 bg-brand-light/60 dark:bg-brand-dark/20 p-2"
			data-testid="selected-section"
		>
			<p class="px-1 pb-1 text-xs font-semibold text-brand dark:text-brand-muted">
				Selected ({selected.size})
			</p>
			<div class="flex flex-wrap gap-1">
				{#each [...selected.values()] as product (product.id)}
					<span
						class="inline-flex items-center gap-1 max-w-full pl-2 pr-1 py-1 text-xs rounded-full bg-white dark:bg-gray-700 border border-gray-200 dark:border-gray-600"
					>
						<span class="truncate max-w-[12rem]">{product.name}</span>
						<button
							type="button"
							onclick={() => selected.delete(product.id)}
							class="p-0.5 rounded-full hover:bg-gray-100 dark:hover:bg-gray-600"
							aria-label="Remove {product.name}"
						>
							<X size={12} />
						</button>
					</span>
				{/each}
			</div>
		</div>
	{/if}

	<div class="flex-1 overflow-y-auto p-4 min-h-[16rem]">
		{#if loading}
			<div class="flex justify-center py-12">
				<LoaderCircle size={28} class="animate-spin text-gray-400" />
			</div>
		{:else if results.length === 0}
			<div class="text-center py-12">
				<Package size={48} class="mx-auto text-gray-300 dark:text-gray-600 mb-4" />
				<p class="text-gray-500 dark:text-gray-400">
					{searchQuery ? 'No products match your search' : 'No products available'}
				</p>
			</div>
		{:else}
			<div class="space-y-2">
				{#each results as product (product.id)}
					{@const inGroup = isInGroup(product.id)}
					{@const isSelected = selected.has(product.id)}
					<button
						type="button"
						onclick={() => toggleSelect(product)}
						disabled={inGroup}
						class="w-full flex items-center gap-3 p-3 rounded-lg border transition-colors text-left {inGroup
							? 'border-gray-200 dark:border-gray-700 opacity-60 cursor-not-allowed'
							: isSelected
								? 'border-brand bg-brand-light dark:bg-brand-dark/30'
								: 'border-gray-200 dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-700'}"
					>
						{#if product.imageUrl}
							<img src={product.imageUrl} alt="" loading="lazy" class="w-12 h-12 object-cover rounded" />
						{:else}
							<div class="w-12 h-12 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center">
								<Package size={20} class="text-gray-400" />
							</div>
						{/if}
						<div class="flex-1 min-w-0">
							<p class="text-sm font-medium text-gray-900 dark:text-white truncate">
								{product.name}
							</p>
							{#if product.currentPrice != null}
								<p class="text-sm text-gray-500 dark:text-gray-400">
									{formatPrice(product.currentPrice, product.currency)}
								</p>
							{/if}
						</div>
						{#if inGroup}
							<span class="text-xs text-gray-400 dark:text-gray-500 shrink-0">Already in group</span>
						{:else if isSelected}
							<div class="p-1 bg-brand rounded-full shrink-0">
								<Check size={14} class="text-white" />
							</div>
						{/if}
					</button>
				{/each}
			</div>

			{#if totalPages > 1}
				<DashboardPagination currentPage={page} {totalPages} onPageChange={handlePageChange} />
			{/if}
		{/if}
	</div>

	<div class="px-6 py-4 border-t border-gray-200 dark:border-gray-700">
		<div class="flex justify-end gap-3">
			<button
				type="button"
				onclick={handleClose}
				disabled={submitting}
				class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
			>
				Cancel
			</button>
			<button
				type="button"
				onclick={handleAdd}
				disabled={submitting || selected.size === 0}
				class="px-4 py-2 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 flex items-center gap-2"
			>
				{#if submitting}
					<LoaderCircle size={16} class="animate-spin" />
				{/if}
				Add{selected.size > 0 ? ` (${selected.size})` : ''}
			</button>
		</div>
	</div>
</Modal>
