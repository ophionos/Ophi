<script lang="ts">
	import type { PageData } from './$types';
	import { goto, replaceState } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import { api, type Product, type GetProductsParams, type ProductListResponse, type ProductStatus } from '$lib/api/client';
	import { products } from '$lib/stores/products.svelte';
	import { viewMode, type ViewMode } from '$lib/stores/viewMode.svelte';
	import { liveRefresh } from '$lib/utils/liveRefresh';
	import { tags } from '$lib/stores/tags.svelte';
	import ProductCard from '$lib/components/products/ProductCard.svelte';
	import ProductTable from '$lib/components/products/ProductTable.svelte';
	import AddProductForm from '$lib/components/products/AddProductForm.svelte';
	import WatchlistFilterBar, { SORT_OPTIONS, type ActiveFilter } from '$lib/components/products/WatchlistFilterBar.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import CreateProductModal from '$lib/components/products/CreateProductModal.svelte';
	import OpportunityChips, { type Opportunity } from '$lib/components/products/OpportunityChips.svelte';
	import ViewModeToggle from '$lib/components/products/ViewModeToggle.svelte';
	import DashboardPagination from '$lib/components/products/DashboardPagination.svelte';
	import ActivityFeed from '$lib/components/products/ActivityFeed.svelte';
	import NeedsActionCard from '$lib/components/products/NeedsActionCard.svelte';
	import ChallengeModal from '$lib/components/products/ChallengeModal.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { ListChecks, Pause, Play, Trash2, ShoppingBag, TrendingDown } from 'lucide-svelte';
	import { fade, fly } from 'svelte/transition';
	import { flip } from 'svelte/animate';
	import OnboardingTour from '$lib/components/shared/OnboardingTour.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';
	import PageHeader from '$lib/components/shared/PageHeader.svelte';

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
	// Account-wide, whatever the filters: the header, the chips and the store select use them.
	let atLowestCount = $state(0);
	let priceDropCount = $state(0);
	let nearTargetCount = $state(0);
	let withAlertsCount = $state(0);
	let productCount = $state(0);
	let urlCount = $state(0);
	let stores = $state<{ storeId: string; name: string }[]>([]);
	// The action card's own query (see +page.ts): it does not follow the table's filters.
	let actionProducts = $state<Product[]>([]);
	let needsActionCount = $state(0);
	let challengeTarget = $state<{ productId: string; urlId: string } | null>(null);

	// Filter state — initialized from the URL so refreshes and shared links land on the same
	// view; every change is mirrored back via syncUrl() (shallow replaceState, no loader re-run).
	const initParams = page.url.searchParams;
	// `filter` holds one quick filter: an opportunity chip, or the action card's "Show in table".
	type QuickFilter = Opportunity;
	const initFilter = initParams.get('filter');
	let opportunity = $state<QuickFilter | null>(initFilter === 'drop' || initFilter === 'lowest' || initFilter === 'near' || initFilter === 'action' ? initFilter : null);
	let search = $state(initParams.get('search') ?? '');
	let status = $state<ProductStatus | ''>((initParams.get('status') as ProductStatus) ?? '');
	// "Favourites first" left the sort (#154): an old link with it gets the default sort.
	const initSort = initParams.get('sortBy');
	let sortBy = $state(initSort === 'favourites' ? '' : (initSort ?? ''));
	let sortDirection = $state(initParams.get('sortDirection') === 'desc' ? 'desc' : 'asc');
	let selectedTagId = $state<string | null>(initParams.get('tag'));
	let storeId = $state<string | null>(initParams.get('store'));
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
		setOrDelete('store', storeId);
		setOrDelete('favourite', favouriteOnly ? 'true' : null);
		setOrDelete('filter', opportunity);
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
			storeId: storeId || undefined,
			page: currentPage,
			// The table and the gallery both draw the sparkline and the range.
			includeSparkline: true,
			priceDrop: opportunity === 'drop' ? true : undefined,
			atLowest: opportunity === 'lowest' ? true : undefined,
			nearTarget: opportunity === 'near' ? true : undefined,
			needsAction: opportunity === 'action' ? true : undefined,
			favourite: favouriteOnly ? true : undefined
		};
	}

	function applyCounts(p: ProductListResponse) {
		atLowestCount = p.atLowestCount;
		priceDropCount = p.priceDropCount;
		nearTargetCount = p.nearTargetCount;
		withAlertsCount = p.withAlertsCount;
		needsActionCount = p.needsActionCount;
		productCount = p.productCount;
		urlCount = p.urlCount;
		stores = p.stores;
	}

	// Hydrate from loader
	$effect(() => {
		const p = pageData.products;
		products.setPage(p.items, p.total, p.page, p.pageSize);
		applyCounts(p);
		actionProducts = pageData.actionProducts.items;
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
			const [productsResult, actionResult] = await Promise.all([
				api.getProducts(buildParams()),
				api.getProducts({ needsAction: true, pageSize: ACTION_CARD_SIZE })
			]);
			if (controller.signal.aborted) return;
			products.setPage(productsResult.items, productsResult.total, productsResult.page, productsResult.pageSize);
			applyCounts(productsResult);
			actionProducts = actionResult.items;
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

	/** Applies a filter or sort change: back to page 1, into the URL, then refetch. */
	function applyFilters(change: () => void) {
		change();
		currentPage = 1;
		syncUrl();
		refreshProducts();
	}

	function setSort(by: string, direction: string) {
		applyFilters(() => {
			sortBy = by;
			sortDirection = direction;
		});
	}

	function clearSearch() {
		if (searchTimeout) clearTimeout(searchTimeout);
		search = '';
		debouncedSearch = '';
	}

	function clearAllFilters() {
		applyFilters(() => {
			clearSearch();
			status = '';
			selectedTagId = null;
			storeId = null;
			favouriteOnly = false;
			opportunity = null;
		});
	}

	function setPage(p: number) {
		currentPage = p;
		syncUrl();
		refreshProducts();
	}

	function setViewMode(mode: ViewMode) {
		viewMode.set(mode);
	}

	// --- Needs your action ---
	async function retryAttention(product: Product) {
		if (!product.attention) return;
		try {
			await api.retryScrapeProductUrl(product.id, product.attention.productUrlId);
			toast.success(`Check queued for ${product.name}`);
			await refreshProducts();
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to queue the check');
		}
	}

	function solveCheck(product: Product) {
		if (product.attention) challengeTarget = { productId: product.id, urlId: product.attention.productUrlId };
	}

	function showActionInTable() {
		viewMode.set('list');
		applyFilters(() => (opportunity = 'action'));
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
		const wasEmpty = productCount === 0;
		const product = await api.addProduct(url);
		products.add(product);
		productCount++;
		urlCount++;
		toast.success('Product added — scraping in progress');
		if (wasEmpty) {
			setTimeout(() => toast.info('Tip: Set a price alert to get notified when the price drops!'), 2000);
		}
	}

	async function handleCreateProduct(data: { name: string; imageUrl?: string; currency?: string }) {
		try {
			const product = await api.createProduct(data);
			products.add(product);
			productCount++;
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
			deleteProductId = null;
			toast.success('Product deleted');
			// ...then reconcile with the server. refreshProducts() aborts any in-flight poll
			// (a pending sibling keeps a 3s poll alive, and its stale response would otherwise
			// re-add the deleted product) and refreshes total and the counts, which optimistic
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

	const STATUS_LABELS: Record<string, string> = { active: 'Active', paused: 'Paused', error: 'Error' };
	const OPPORTUNITY_LABELS: Record<QuickFilter, string> = { drop: 'Price dropped (7 days)', lowest: 'At a real low', near: 'Alert near target', action: 'Needs your action' };
	const ACTION_CARD_SIZE = 10;

	// Every filter in effect, in the order of the bar. The sort is not a filter: it hides nothing.
	const activeFilters = $derived.by(() => {
		const list: ActiveFilter[] = [];
		if (debouncedSearch)
			list.push({ key: 'search', label: `Text: "${debouncedSearch}"`, onRemove: () => applyFilters(clearSearch) });
		if (status)
			list.push({ key: 'status', label: `Status: ${STATUS_LABELS[status] ?? status}`, onRemove: () => applyFilters(() => (status = '')) });
		if (selectedTagId) {
			const name = tags.items.find((t) => t.id === selectedTagId)?.name ?? 'unknown';
			list.push({ key: 'tag', label: `Tag: ${name}`, onRemove: () => applyFilters(() => (selectedTagId = null)) });
		}
		if (storeId) {
			const name = stores.find((s) => s.storeId === storeId)?.name ?? storeId;
			list.push({ key: 'store', label: `Store: ${name}`, onRemove: () => applyFilters(() => (storeId = null)) });
		}
		if (favouriteOnly)
			list.push({ key: 'favourite', label: 'Favourites', onRemove: () => applyFilters(() => (favouriteOnly = false)) });
		if (opportunity)
			list.push({ key: 'opportunity', label: OPPORTUNITY_LABELS[opportunity], onRemove: () => applyFilters(() => (opportunity = null)) });
		return list;
	});

	const hasActiveFilters = $derived(activeFilters.length > 0);
	const isEmptyAccount = $derived(productCount === 0 && products.items.length === 0);
	const emptyFilterMessage = $derived(`No products match: ${activeFilters.map((f) => f.label).join(', ')}`);
	// A sort the select does not offer (an old link) shows as the default instead of a blank select.
	const knownSort = $derived(SORT_OPTIONS.some((o) => o.value === `${sortBy}:${sortDirection}`));

	const totalPages = $derived(Math.max(1, Math.ceil(products.total / products.pageSize)));
</script>

<svelte:head>
	<title>Watchlist - Ophi</title>
</svelte:head>

<div>
	<!-- The paste-URL field: a compact field in the header once products exist; the whole row
	     while the account is empty, where it is the only thing to do. -->
	{#snippet addProduct()}
		<div class="flex flex-col gap-1 {isEmptyAccount ? 'mb-3 sm:flex-row sm:items-center sm:gap-3' : 'w-[min(28rem,calc(100vw-2rem))]'}" data-onboarding="add-product">
			<div class="w-full flex-1">
				<AddProductForm onSubmit={handleAddProduct} />
			</div>
			<button
				type="button"
				onclick={() => (showCreateModal = true)}
				class="min-h-11 shrink-0 self-start whitespace-nowrap text-xs text-brand dark:text-brand-muted hover:text-brand-hover dark:hover:text-brand-muted hover:underline sm:self-auto {isEmptyAccount ? '' : 'sm:self-end'}"
				data-testid="create-from-scratch"
			>
				or create from scratch
			</button>
		</div>
	{/snippet}

	{#if isEmptyAccount}
		<PageHeader title="Watchlist" description="Paste a product URL to start tracking its price" />
		{@render addProduct()}
	{:else}
		<PageHeader
			title="Watchlist"
			description={`${productCount} ${productCount === 1 ? 'product' : 'products'} · ${urlCount} ${urlCount === 1 ? 'URL' : 'URLs'}`}
			actions={addProduct}
		/>

		<NeedsActionCard
			products={actionProducts}
			count={needsActionCount}
			challengeAvailable={pageData.challengeAvailable}
			onRetry={retryAttention}
			onSolveCheck={solveCheck}
			onShowInTable={showActionInTable}
		/>

		<OpportunityChips
			{priceDropCount}
			{needsActionCount}
			{atLowestCount}
			{nearTargetCount}
			active={opportunity}
			onSelect={(next) => applyFilters(() => (opportunity = next))}
		/>
	{/if}

	<div>
		{#if !isEmptyAccount}
		<WatchlistFilterBar
			{search}
			{status}
			{selectedTagId}
			{storeId}
			sortBy={knownSort ? sortBy : ''}
			{sortDirection}
			{favouriteOnly}
			tags={tags.items}
			{stores}
			{activeFilters}
			onSearchChange={handleSearchChange}
			onStatusChange={(value) => applyFilters(() => (status = value))}
			onTagChange={(value) => applyFilters(() => (selectedTagId = value))}
			onStoreChange={(value) => applyFilters(() => (storeId = value))}
			onSortChange={setSort}
			onFavouriteChange={(value) => applyFilters(() => (favouriteOnly = value))}
			onClearAll={clearAllFilters}
		>
			{#snippet trailing()}
				<div class="flex items-center gap-2">
						<button
							onclick={toggleSelectionMode}
							class="flex min-h-11 items-center gap-1.5 px-3 text-sm rounded-lg border transition-colors {selectionMode
								? 'bg-brand text-white border-brand'
								: 'text-gray-600 dark:text-gray-400 border-gray-300 dark:border-gray-600 hover:bg-surface-2'}"
							data-testid="bulk-select-toggle"
							aria-pressed={selectionMode}
						>
							<ListChecks size={15} />
							{selectionMode ? 'Done' : 'Select'}
						</button>
					<ViewModeToggle mode={viewMode.current} onModeChange={setViewMode} />
				</div>
			{/snippet}
		</WatchlistFilterBar>
		{/if}

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
					class="min-h-11 text-sm text-brand dark:text-brand-muted hover:underline"
					data-testid="bulk-select-page"
				>
					Select page
				</button>
				<div class="flex-1"></div>
				<button
					onclick={() => bulkSetStatus('paused')}
					disabled={bulkBusy || selectedIds.size === 0}
					class="flex min-h-11 items-center gap-1 px-3 text-sm rounded-md text-gray-700 dark:text-gray-300 hover:bg-surface-2 disabled:opacity-50"
					data-testid="bulk-pause"
				>
					<Pause size={14} />
					Pause
				</button>
				<button
					onclick={() => bulkSetStatus('active')}
					disabled={bulkBusy || selectedIds.size === 0}
					class="flex min-h-11 items-center gap-1 px-3 text-sm rounded-md text-gray-700 dark:text-gray-300 hover:bg-surface-2 disabled:opacity-50"
					data-testid="bulk-resume"
				>
					<Play size={14} />
					Resume
				</button>
				<button
					onclick={() => (bulkDeleteConfirmOpen = true)}
					disabled={bulkBusy || selectedIds.size === 0}
					class="flex min-h-11 items-center gap-1 px-3 text-sm rounded-md text-danger hover:bg-danger/10 disabled:opacity-50"
					data-testid="bulk-delete"
				>
					<Trash2 size={14} />
					Delete
				</button>
			</div>
		{/if}

		<!-- The rail sits beside the gallery only: the table needs the full frame width, so in
		     table view the feed drops below it. min-w-0 keeps a wide child inside its column. -->
		<div class="grid gap-6 {!isEmptyAccount && viewMode.current === 'grid' ? 'lg:grid-cols-[minmax(0,1fr)_20rem]' : ''}">
		<div class="min-w-0">
		{#if loadError}
			<div class="text-center py-12 bg-surface-1 rounded-lg border border-danger/30" data-testid="load-error">
				<p role="alert" class="text-danger mb-2">{loadError}</p>
				<button
					onclick={() => refreshProducts()}
					class="text-sm text-brand dark:text-brand-muted hover:underline"
				>
					Try again
				</button>
			</div>
		{:else if products.items.length === 0 && hasActiveFilters}
			<div
				class="text-center py-12 bg-surface-1 rounded-lg border border-gray-200 dark:border-gray-700"
				data-testid="empty-filter-result"
			>
				<p class="text-gray-500 dark:text-gray-400">{emptyFilterMessage}</p>
				<button
					type="button"
					onclick={clearAllFilters}
					class="mt-3 min-h-11 px-3 text-sm text-brand dark:text-brand-muted hover:underline"
					data-testid="empty-clear-filters"
				>
					Clear filters
				</button>
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
			<div class="grid sm:grid-cols-2 xl:grid-cols-3 gap-4" in:fade={{ duration: 150 }}>
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
		{:else}
			<div in:fade={{ duration: 150 }}>
			<ProductTable
				products={products.items}
				{sortBy}
				{sortDirection}
				onSortChange={setSort}
				hasAlerts={withAlertsCount > 0}
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

		{#if !isEmptyAccount}
			<aside class="min-w-0">
				<ActivityFeed products={products.items} />
			</aside>
		{/if}
		</div>
	</div>
</div>

{#if challengeTarget}
	<ChallengeModal
		isOpen
		productId={challengeTarget.productId}
		urlId={challengeTarget.urlId}
		onClose={() => (challengeTarget = null)}
		onSolved={() => {
			challengeTarget = null;
			refreshProducts();
		}}
	/>
{/if}

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
