<script lang="ts">
	import type { PageData } from './$types';
	import { goto, replaceState } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import { api, type Product, type GetProductsParams, type ProductStatus } from '$lib/api/client';
	import { products } from '$lib/stores/products.svelte';
	import { viewMode, type ViewMode } from '$lib/stores/viewMode.svelte';
	import { liveRefresh } from '$lib/utils/liveRefresh';
	import { tags } from '$lib/stores/tags.svelte';
	import ProductCard from '$lib/components/products/ProductCard.svelte';
	import ProductTable from '$lib/components/products/ProductTable.svelte';
	import AddProductForm from '$lib/components/products/AddProductForm.svelte';
	import ProductFilters from '$lib/components/products/ProductFilters.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import CreateProductModal from '$lib/components/products/CreateProductModal.svelte';
	import DashboardStatsBar from '$lib/components/products/DashboardStatsBar.svelte';
	import ViewModeToggle from '$lib/components/products/ViewModeToggle.svelte';
	import DensityToggle from '$lib/components/products/DensityToggle.svelte';
	import DashboardPagination from '$lib/components/products/DashboardPagination.svelte';
	import ActivityFeed from '$lib/components/products/ActivityFeed.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { ListChecks, Pause, Play, Trash2, ShoppingBag, TrendingDown } from 'lucide-svelte';
	import { fade, fly } from 'svelte/transition';
	import { flip } from 'svelte/animate';
	import OnboardingTour from '$lib/components/shared/OnboardingTour.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	let { data: pageData }: { data: PageData } = $props();

	let deleteProductId = $state<string | null>(null);
	let deleteLoading = $state(false);
	let showCreateModal = $state(false);

	// Bulk selection
	let selectionMode = $state(false);
	let selectedIds = $state<ReadonlySet<string>>(new Set());
	let bulkBusy = $state(false);
	let bulkDeleteConfirmOpen = $state(false);

	let loadError = $state('');
	let atLowestCount = $state(0);
	let priceDropCount = $state(0);
	let withAlertsCount = $state(0);
	let totalProducts = $state(0);

	// Filter state — initialized from the URL so refreshes and shared links land on the same
	// view; every change is mirrored back via syncUrl() (shallow replaceState, no loader re-run).
	const initParams = page.url.searchParams;
	const initFilter = initParams.get('filter');
	let statsFilter = $state<null | 'drops' | 'alerts' | 'lowest'>(
		initFilter === 'drops' || initFilter === 'alerts' || initFilter === 'lowest' ? initFilter : null
	);
	let search = $state(initParams.get('search') ?? '');
	let status = $state<ProductStatus | ''>((initParams.get('status') as ProductStatus) ?? '');
	let sortBy = $state(initParams.get('sortBy') ?? '');
	let sortDirection = $state(initParams.get('sortDirection') === 'desc' ? 'desc' : 'asc');
	let selectedTagId = $state<string | null>(initParams.get('tag'));
	let favouriteOnly = $state(initParams.get('favourite') === 'true');
	let currentPage = $state(Math.max(1, parseInt(initParams.get('page') ?? '1', 10) || 1));

	// Debounced search
	let searchTimeout: ReturnType<typeof setTimeout> | null = null;
	let debouncedSearch = $state(initParams.get('search') ?? '');
	let inFlight: AbortController | null = null;

	// Mirror the current filters into the address bar. Shallow on purpose: replaceState neither
	// re-runs the loader (refreshProducts already fetched) nor pollutes history with every keystroke.
	function syncUrl() {
		const url = new URL(page.url);
		const sp = url.searchParams;
		const setOrDelete = (key: string, value: string | null) =>
			value ? sp.set(key, value) : sp.delete(key);
		setOrDelete('search', debouncedSearch || null);
		setOrDelete('status', status || null);
		setOrDelete('sortBy', sortBy || null);
		setOrDelete('sortDirection', sortBy && sortDirection === 'desc' ? 'desc' : null);
		setOrDelete('tag', selectedTagId);
		setOrDelete('favourite', favouriteOnly ? 'true' : null);
		setOrDelete('filter', statsFilter);
		setOrDelete('page', currentPage > 1 ? String(currentPage) : null);
		replaceState(url, {});
	}

	const POLL_INTERVAL_MS = 3000;
	const DEBOUNCE_MS = 300;

	// Check if any products are pending
	const hasPendingProducts = $derived(products.items.some((p) => p.status === 'pending'));

	function buildParams(): GetProductsParams {
		return {
			search: debouncedSearch || undefined,
			status: (status || undefined) as ProductStatus | undefined,
			sortBy: sortBy || undefined,
			sortDirection: sortBy ? sortDirection : undefined,
			tagId: selectedTagId || undefined,
			page: currentPage,
			includeSparkline: viewMode.current === 'grid' ? true : undefined,
			atLowest: statsFilter === 'lowest' ? true : undefined,
			favourite: favouriteOnly ? true : undefined,
			priceDrop: statsFilter === 'drops' ? true : undefined,
			hasAlerts: statsFilter === 'alerts' ? true : undefined
		};
	}

	// Applied once per page load, not on every hydrate: `total` narrows as filters are applied,
	// and searching down to three results shouldn't flip the view back to grid.
	let viewDefaultApplied = false;

	// Hydrate from loader
	$effect(() => {
		const p = pageData.products;
		products.setPage(p.items, p.total, p.page, p.pageSize);
		totalProducts = p.total;
		if (!viewDefaultApplied) {
			viewDefaultApplied = true;
			viewMode.applyCountDefault(p.total);
		}
		atLowestCount = p.atLowestCount;
		priceDropCount = p.priceDropCount;
		withAlertsCount = p.withAlertsCount;
		tags.set(pageData.tags);
	});

	// Live updates: refresh on completed scrapes, with a pending-only poll while SSE is down.
	$effect(() =>
		liveRefresh({
			matches: (evt) => evt.type === 'scrape-completed',
			refresh: refreshProducts,
			shouldPoll: () => hasPendingProducts,
			intervalMs: POLL_INTERVAL_MS
		})
	);

	async function refreshProducts() {
		inFlight?.abort();
		const controller = new AbortController();
		inFlight = controller;
		try {
			const productsResult = await api.getProducts(buildParams());
			if (controller.signal.aborted) return;
			products.setPage(productsResult.items, productsResult.total, productsResult.page, productsResult.pageSize);
			totalProducts = productsResult.total;
			atLowestCount = productsResult.atLowestCount;
			priceDropCount = productsResult.priceDropCount;
			withAlertsCount = productsResult.withAlertsCount;
		} catch (error) {
			if (controller.signal.aborted) return;
			console.error('Failed to refresh products:', error);
		} finally {
			if (inFlight === controller) inFlight = null;
		}
	}

	function handleSearchChange(value: string) {
		search = value;
		if (searchTimeout) clearTimeout(searchTimeout);
		searchTimeout = setTimeout(() => {
			debouncedSearch = value;
			currentPage = 1;
			syncUrl();
			refreshProducts();
		}, DEBOUNCE_MS);
	}

	function handleFilterStatusChange(value: string) {
		status = value as ProductStatus | '';
		currentPage = 1;
		syncUrl();
		refreshProducts();
	}

	function handleSortChange(newSortBy: string, newSortDirection: string) {
		sortBy = newSortBy;
		sortDirection = newSortDirection;
		currentPage = 1;
		syncUrl();
		refreshProducts();
	}

	function handleTagChange(tagId: string | null) {
		selectedTagId = tagId;
		currentPage = 1;
		syncUrl();
		refreshProducts();
	}

	function handleFavouriteChange(value: boolean) {
		favouriteOnly = value;
		currentPage = 1;
		syncUrl();
		refreshProducts();
	}

	function setStatsFilter(filter: typeof statsFilter) {
		statsFilter = filter;
		currentPage = 1;
		syncUrl();
		refreshProducts();
	}

	function setPage(p: number) {
		currentPage = p;
		syncUrl();
		refreshProducts();
	}

	function setViewMode(mode: ViewMode) {
		// Only grid carries sparkline data, so crossing the grid boundary needs a refetch.
		const needsRefresh = (mode === 'grid') !== (viewMode.current === 'grid');
		viewMode.set(mode);
		if (needsRefresh) refreshProducts();
	}

	// --- Bulk selection ---
	function toggleSelectionMode() {
		selectionMode = !selectionMode;
		selectedIds = new Set();
	}

	function toggleSelected(id: string) {
		const next = new Set(selectedIds);
		if (next.has(id)) next.delete(id);
		else next.add(id);
		selectedIds = next;
	}

	function selectPage() {
		selectedIds = new Set(products.items.map((p) => p.id));
	}

	async function bulkSetStatus(newStatus: 'active' | 'paused') {
		const ids = [...selectedIds];
		if (ids.length === 0) return;
		bulkBusy = true;
		const results = await Promise.allSettled(
			ids.map((id) => api.updateProduct(id, { status: newStatus }))
		);
		bulkBusy = false;
		const failed = results.filter((r) => r.status === 'rejected').length;
		if (failed > 0) {
			toast.error(`${failed} of ${ids.length} products failed to update`);
		} else {
			toast.success(`${ids.length} product${ids.length === 1 ? '' : 's'} ${newStatus === 'paused' ? 'paused' : 'resumed'}`);
		}
		selectedIds = new Set();
		await refreshProducts();
	}

	async function confirmBulkDelete() {
		const ids = [...selectedIds];
		if (ids.length === 0) return;
		bulkBusy = true;
		const results = await Promise.allSettled(ids.map((id) => api.deleteProduct(id)));
		bulkBusy = false;
		bulkDeleteConfirmOpen = false;
		const failed = results.filter((r) => r.status === 'rejected').length;
		if (failed > 0) {
			toast.error(`${failed} of ${ids.length} products failed to delete`);
		} else {
			toast.success(`${ids.length} product${ids.length === 1 ? '' : 's'} deleted`);
		}
		selectedIds = new Set();
		await refreshProducts();
	}

	async function handleAddProduct(url: string) {
		const wasEmpty = totalProducts === 0;
		const product = await api.addProduct(url);
		products.add(product);
		totalProducts++;
		toast.success('Product added — scraping in progress');
		if (wasEmpty) {
			setTimeout(() => toast.info('Tip: Set a price alert to get notified when the price drops!'), 2000);
		}
	}

	async function handleCreateProduct(data: { name: string; imageUrl?: string; currency?: string }) {
		try {
			const product = await api.createProduct(data);
			products.add(product);
			totalProducts++;
			showCreateModal = false;
			goto(resolve(`/products/${product.id}`));
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to create product');
		}
	}

	function handleDeleteProduct(id: string) {
		deleteProductId = id;
	}

	async function confirmDeleteProduct() {
		if (!deleteProductId) return;
		deleteLoading = true;
		try {
			await api.deleteProduct(deleteProductId);
			// Optimistic removal for instant feedback...
			products.remove(deleteProductId);
			totalProducts--;
			deleteProductId = null;
			toast.success('Product deleted');
			// ...then reconcile with the server. refreshProducts() aborts any in-flight poll
			// (a pending sibling keeps a 3s poll alive, and its stale response would otherwise
			// re-add the deleted product) and refreshes total/atLowestCount, which optimistic
			// removal alone leaves stale.
			await refreshProducts();
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to delete product');
		} finally {
			deleteLoading = false;
		}
	}

	async function handleProductStatusChange(id: string, newStatus: ProductStatus) {
		try {
			const updated = await api.updateProduct(id, { status: newStatus });
			products.update(id, updated);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update product status');
		}
	}

	async function handleFavouriteToggle(id: string, isFavourite: boolean) {
		try {
			const updated = await api.updateProduct(id, { isFavourite });
			products.update(id, updated);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update favourite');
		}
	}

	function handleAlertCreated() {
		// Re-pull so the global with-alerts count reflects the server, not a local guess.
		refreshProducts();
	}

	function handleViewHistory(product: Product) {
		goto(resolve(`/products/${product.id}`));
	}

	const hasActiveFilters = $derived(!!status || !!debouncedSearch || !!selectedTagId || favouriteOnly || !!statsFilter);

	const emptyFilterMessage = $derived.by(() => {
		if (statsFilter === 'drops') return 'No products with recent price drops';
		if (statsFilter === 'alerts') return 'No products with active alerts';
		if (statsFilter === 'lowest') return 'No products currently at their lowest price';
		if (favouriteOnly) return 'No favourite products found';
		if (status === 'paused') return 'No paused products';
		if (status === 'error') return 'No products with errors';
		if (status === 'active') return 'No active products';
		if (debouncedSearch) return `No products matching "${debouncedSearch}"`;
		if (selectedTagId) return 'No products with the selected tag';
		return 'No products found';
	});

	const totalPages = $derived(Math.max(1, Math.ceil(products.total / products.pageSize)));
</script>

<svelte:head>
	<title>Dashboard - Ophi</title>
</svelte:head>

<div>
	<DashboardStatsBar
		{totalProducts}
		priceDrops={priceDropCount}
		alertCount={withAlertsCount}
		{atLowestCount}
		{statsFilter}
		onFilterChange={setStatsFilter}
	/>

	<!-- Slim inline add product bar — the "create from scratch" affordance sits on the same row
	     as the paste-URL input (instead of a separate line below) to keep the header compact. -->
	<div
		class="mb-3 flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-3"
		data-onboarding="add-product"
	>
		<div class="w-full flex-1">
			<AddProductForm onSubmit={handleAddProduct} />
		</div>
		<button
			type="button"
			onclick={() => (showCreateModal = true)}
			class="shrink-0 whitespace-nowrap text-xs text-brand dark:text-brand-muted hover:text-brand-hover dark:hover:text-brand-muted hover:underline"
			data-testid="create-from-scratch"
		>
			or create from scratch
		</button>
	</div>

	<div>
		<ProductFilters
			{search}
			{status}
			{sortBy}
			{sortDirection}
			{favouriteOnly}
			tags={tags.items}
			{selectedTagId}
			onSearchChange={handleSearchChange}
			onStatusChange={handleFilterStatusChange}
			onSortChange={handleSortChange}
			onFavouriteChange={handleFavouriteChange}
			onTagChange={handleTagChange}
		>
			{#snippet leading()}
				<h2 class="text-lg font-semibold text-gray-900 dark:text-white whitespace-nowrap">
					Your Products
				</h2>
			{/snippet}
			{#snippet trailing()}
				<div class="flex items-center gap-2">
					{#if viewMode.current !== 'feed'}
						<button
							onclick={toggleSelectionMode}
							class="flex items-center gap-1.5 px-2.5 py-1.5 text-sm rounded-lg border transition-colors {selectionMode
								? 'bg-brand text-white border-brand'
								: 'text-gray-600 dark:text-gray-400 border-gray-300 dark:border-gray-600 hover:bg-surface-2'}"
							data-testid="bulk-select-toggle"
							aria-pressed={selectionMode}
						>
							<ListChecks size={15} />
							{selectionMode ? 'Done' : 'Select'}
						</button>
					{/if}
					{#if viewMode.current === 'list'}
						<!-- Desktop table only; the mobile card stack ignores density. -->
						<div class="hidden md:flex">
							<DensityToggle />
						</div>
					{/if}
					<ViewModeToggle mode={viewMode.current} onModeChange={setViewMode} />
				</div>
			{/snippet}
		</ProductFilters>

		{#if selectionMode}
			<div
				class="flex flex-wrap items-center gap-2 mb-3 px-3 py-2 bg-brand-light dark:bg-brand-dark/20 border border-brand/20 rounded-lg"
				data-testid="bulk-bar"
				in:fade={{ duration: 150 }}
			>
				<span class="text-sm font-medium text-gray-900 dark:text-white">
					{selectedIds.size} selected
				</span>
				<button
					onclick={selectPage}
					class="text-sm text-brand dark:text-brand-muted hover:underline"
					data-testid="bulk-select-page"
				>
					Select page
				</button>
				<div class="flex-1"></div>
				<button
					onclick={() => bulkSetStatus('paused')}
					disabled={bulkBusy || selectedIds.size === 0}
					class="flex items-center gap-1 px-2.5 py-1 text-sm rounded-md text-gray-700 dark:text-gray-300 hover:bg-surface-2 disabled:opacity-50"
					data-testid="bulk-pause"
				>
					<Pause size={14} />
					Pause
				</button>
				<button
					onclick={() => bulkSetStatus('active')}
					disabled={bulkBusy || selectedIds.size === 0}
					class="flex items-center gap-1 px-2.5 py-1 text-sm rounded-md text-gray-700 dark:text-gray-300 hover:bg-surface-2 disabled:opacity-50"
					data-testid="bulk-resume"
				>
					<Play size={14} />
					Resume
				</button>
				<button
					onclick={() => (bulkDeleteConfirmOpen = true)}
					disabled={bulkBusy || selectedIds.size === 0}
					class="flex items-center gap-1 px-2.5 py-1 text-sm rounded-md text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 disabled:opacity-50"
					data-testid="bulk-delete"
				>
					<Trash2 size={14} />
					Delete
				</button>
			</div>
		{/if}

		{#if loadError}
			<div class="text-center py-12 bg-white dark:bg-gray-800 rounded-lg border border-red-200 dark:border-red-800" data-testid="load-error">
				<p role="alert" class="text-red-600 dark:text-red-400 mb-2">{loadError}</p>
				<button
					onclick={() => refreshProducts()}
					class="text-sm text-brand dark:text-brand-muted hover:underline"
				>
					Try again
				</button>
			</div>
		{:else if products.items.length === 0 && hasActiveFilters}
			<div
				class="text-center py-12 bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700"
				data-testid="empty-filter-result"
			>
				<p class="text-gray-500 dark:text-gray-400">{emptyFilterMessage}</p>
			</div>
		{:else if products.items.length === 0}
			<EmptyState
				size="lg"
				icon={ShoppingBag}
				badgeIcon={TrendingDown}
				title="Start tracking prices"
				description="Add your first product to start tracking prices and get notified when they drop."
			>
				<p class="text-sm text-gray-400 dark:text-gray-500">Paste a product URL above to get started</p>
			</EmptyState>
		{:else if viewMode.current === 'grid'}
			<div class="grid md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4" in:fade={{ duration: 150 }}>
				{#each products.items as product (product.id)}
					<div animate:flip={{ duration: 400 }} in:fly={{ y: 20, duration: 400, delay: 50 }} out:fade={{ duration: 200 }}>
						<ProductCard
							{product}
							onDelete={handleDeleteProduct}
							onViewHistory={handleViewHistory}
							onStatusChange={handleProductStatusChange}
							onFavouriteToggle={handleFavouriteToggle}
							onAlertCreated={handleAlertCreated}
							selectable={selectionMode}
							selected={selectedIds.has(product.id)}
							onSelectToggle={toggleSelected}
						/>
					</div>
				{/each}
			</div>
		{:else if viewMode.current === 'feed'}
			<div in:fade={{ duration: 150 }}>
				<ActivityFeed products={products.items} />
			</div>
		{:else}
			<div in:fade={{ duration: 150 }}>
			<ProductTable
				products={products.items}
				onDelete={handleDeleteProduct}
				onViewHistory={handleViewHistory}
				onStatusChange={handleProductStatusChange}
				onFavouriteToggle={handleFavouriteToggle}
				onAlertCreated={handleAlertCreated}
				selectable={selectionMode}
				{selectedIds}
				onSelectToggle={toggleSelected}
			/>
			</div>
		{/if}

		{#if totalPages > 1 && products.items.length > 0}
			<DashboardPagination {currentPage} {totalPages} onPageChange={setPage} />
		{/if}
	</div>
</div>

<ConfirmModal
	isOpen={deleteProductId !== null}
	title="Delete Product"
	message="Are you sure you want to delete this product? All price history, alerts, and notifications for this product will be permanently removed."
	onConfirm={confirmDeleteProduct}
	onCancel={() => (deleteProductId = null)}
	loading={deleteLoading}
/>

<ConfirmModal
	isOpen={bulkDeleteConfirmOpen}
	title="Delete {selectedIds.size} Product{selectedIds.size === 1 ? '' : 's'}"
	message="Are you sure you want to delete the selected products? All their price history, alerts, and notifications will be permanently removed."
	confirmText="Delete"
	onConfirm={confirmBulkDelete}
	onCancel={() => (bulkDeleteConfirmOpen = false)}
	loading={bulkBusy}
/>

<CreateProductModal
	isOpen={showCreateModal}
	onClose={() => (showCreateModal = false)}
	onSave={handleCreateProduct}
/>

<OnboardingTour />
