<script lang="ts">
	import type { PageData } from './$types';
	import { page } from '$app/state';
	import { replaceState } from '$app/navigation';
	import { onMount } from 'svelte';
	import {
		api,
		type Store,
		type CreateStoreRequest,
		type UpdateStoreRequest,
		type StoreImportRequest
	} from '$lib/api/client';
	import { stores } from '$lib/stores/stores.svelte';
	import StoreCard from '$lib/components/stores/StoreCard.svelte';
	import StoreModal from '$lib/components/stores/StoreModal.svelte';
	import TestStoreModal from '$lib/components/stores/TestStoreModal.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import StatCard from '$lib/components/shared/StatCard.svelte';
	import ImportStoreModal from '$lib/components/stores/ImportStoreModal.svelte';
	import { Store as StoreIcon, Plus, Sparkles, Upload } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { downloadBlob } from '$lib/utils/download';
	import { findStoreForDomain } from '$lib/utils/storeMatch';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	let { data: pageData }: { data: PageData } = $props();

	let loadError = $state('');
	let modalOpen = $state(false);
	let editingStore = $state<Store | undefined>(undefined);
	let deleteModalOpen = $state(false);
	let deletingStore = $state<Store | undefined>(undefined);
	let deleteLoading = $state(false);
	let testModalOpen = $state(false);
	let testingStore = $state<Store | undefined>(undefined);
	let importModalOpen = $state(false);

	$effect(() => {
		stores.set(pageData.stores);
	});

	// Auto-open the edit modal once on mount when arrived at via ?edit=<domain> (e.g. the
	// store-config gear on a product page). Deliberately NOT inside the data-sync $effect
	// above: that effect writes `stores`, and reading `stores.items` back in the same effect
	// created a write→read cycle that looped forever (effect_update_depth_exceeded).
	onMount(() => {
		openStoreFromQueryParam();
	});

	function openStoreFromQueryParam() {
		const editDomain = page.url.searchParams.get('edit');
		if (!editDomain) return;

		// Match against the loader data directly (not the reactive `stores` store) to keep this
		// fully decoupled from the data-sync effect.
		const match = findStoreForDomain(pageData.stores, editDomain);

		// Built-in stores are not editable (they have no DB row to update), so never auto-open
		// the editor for them — this enforces the invariant regardless of how the ?edit link was
		// produced (the product-page gear, a stale bookmark, or a hand-typed URL).
		if (match && !match.isBuiltIn) {
			handleOpenEdit(match);
		}

		// Strip ?edit from the URL so closing the modal can't be reopened by a re-render and a
		// manual refresh doesn't re-trigger the auto-open.
		const url = new URL(page.url);
		url.searchParams.delete('edit');
		replaceState(url, {});
	}

	function handleOpenCreate() {
		editingStore = undefined;
		modalOpen = true;
	}

	function handleOpenEdit(store: Store) {
		editingStore = store;
		modalOpen = true;
	}

	function handleCloseModal() {
		modalOpen = false;
		editingStore = undefined;
	}

	async function handleSave(data: CreateStoreRequest | UpdateStoreRequest) {
		try {
			if (editingStore) {
				const updated = await api.updateStore(editingStore.id!, data as UpdateStoreRequest);
				stores.update(editingStore.storeId, updated);
				toast.success(`Store "${updated.name}" updated`);
			} else {
				const created = await api.createStore(data as CreateStoreRequest);
				stores.add(created);
				toast.success(`Store "${created.name}" created`);
			}
			handleCloseModal();
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to save store');
		}
	}

	function handleOpenTest(store: Store) {
		testingStore = store;
		testModalOpen = true;
	}

	function handleCloseTest() {
		testModalOpen = false;
		testingStore = undefined;
	}

	function handleOpenDelete(store: Store) {
		deletingStore = store;
		deleteModalOpen = true;
	}

	function handleCloseDelete() {
		deleteModalOpen = false;
		deletingStore = undefined;
		deleteLoading = false;
	}

	async function handleConfirmDelete() {
		if (!deletingStore?.id) return;

		deleteLoading = true;
		try {
			await api.deleteStore(deletingStore.id);
			stores.remove(deletingStore.storeId);
			toast.success(`Store "${deletingStore.name}" deleted`);
			handleCloseDelete();
		} catch (error) {
			console.error('Failed to delete store:', error);
			toast.error('Failed to delete store');
			deleteLoading = false;
		}
	}

	async function handleExport(store: Store) {
		if (!store.id) return;
		try {
			const exported = await api.exportStore(store.id);
			const blob = new Blob([JSON.stringify(exported, null, 2)], { type: 'application/json' });
			downloadBlob(blob, `${store.storeId}.json`);
			toast.success(`Store "${store.name}" exported`);
		} catch (err) {
			console.error('Failed to export store:', err);
			toast.error('Failed to export store');
		}
	}

	async function handleImport(data: StoreImportRequest) {
		try {
			await api.importStore(data);
			importModalOpen = false;
			// importStore returns only a partial store; re-fetch the full list so the
			// imported store renders with all fields.
			const refreshed = await api.getStores();
			stores.set(refreshed.items);
			toast.success('Store configuration imported');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to import store');
		}
	}

	type StoreFilter = 'all' | 'built-in' | 'auto-detected' | 'custom';
	let storeFilter = $state<StoreFilter>('all');

	const stats = $derived({
		total: stores.items.length,
		builtIn: stores.items.filter((s) => s.isBuiltIn).length,
		custom: stores.items.filter((s) => !s.isBuiltIn && !s.isAutoCreated).length,
		autoCreated: stores.items.filter((s) => s.isAutoCreated && !s.isBuiltIn).length
	});

	const filteredStores = $derived(
		storeFilter === 'all'
			? stores.items
			: storeFilter === 'built-in'
				? stores.items.filter((s) => s.isBuiltIn)
				: storeFilter === 'auto-detected'
					? stores.items.filter((s) => s.isAutoCreated && !s.isBuiltIn)
					: stores.items.filter((s) => !s.isBuiltIn && !s.isAutoCreated)
	);

	const storeFilters = $derived(
		[
			{ value: 'all', label: 'All', count: stats.total },
			{ value: 'built-in', label: 'Built-in', count: stats.builtIn },
			{ value: 'auto-detected', label: 'Auto-detected', count: stats.autoCreated },
			{ value: 'custom', label: 'Custom', count: stats.custom }
		] satisfies { value: StoreFilter; label: string; count: number }[]
	);
</script>

<svelte:head>
	<title>Store Configurations - Ophi</title>
</svelte:head>

<div>
	<div class="mb-8 flex items-start justify-between">
		<div>
			<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">Store Configurations</h1>
			<p class="text-gray-600 dark:text-gray-400">
				Manage CSS selectors for scraping product data from different stores
			</p>
		</div>
		<div class="flex gap-2">
			<button
				onclick={() => (importModalOpen = true)}
				class="px-4 py-2 bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-200 dark:hover:bg-gray-600 flex items-center gap-2"
			>
				<Upload size={18} />
				Import
			</button>
			<button
				onclick={handleOpenCreate}
				class="px-4 py-2 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
			>
				<Plus size={18} />
				Add Store
			</button>
		</div>
	</div>

	<div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
		<StatCard
			label="Total Stores"
			value={stats.total}
			icon={StoreIcon}
			iconClass="text-purple-600 dark:text-purple-400"
			testid="stat-total-stores"
		/>
		<StatCard
			label="Built-in"
			value={stats.builtIn}
			icon={StoreIcon}
			iconClass="text-brand dark:text-brand-muted"
			testid="stat-built-in-stores"
		/>
		<StatCard
			label="Custom"
			value={stats.custom}
			icon={StoreIcon}
			iconClass="text-green-600 dark:text-green-400"
			testid="stat-custom-stores"
		/>
		<StatCard
			label="Auto-detected"
			value={stats.autoCreated}
			icon={Sparkles}
			iconClass="text-amber-600 dark:text-amber-400"
		/>
	</div>

	<div class="flex flex-wrap gap-2 mb-6" data-testid="store-filters">
		{#each storeFilters as opt (opt.value)}
			<button
				onclick={() => (storeFilter = opt.value)}
				class="px-4 py-1.5 rounded-full text-sm font-medium transition-colors {storeFilter === opt.value
					? 'bg-brand text-white'
					: 'bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}"
				data-testid="store-filter-{opt.value}"
			>
				{opt.label} ({opt.count})
			</button>
		{/each}
	</div>

	<div>
		{#if loadError}
			<div class="text-center py-12 bg-white dark:bg-gray-800 rounded-lg border border-red-200 dark:border-red-800">
				<p role="alert" class="text-red-600 dark:text-red-400 mb-2">{loadError}</p>
			</div>
		{:else if stores.items.length === 0}
			<EmptyState
				icon={StoreIcon}
				title="No stores configured"
				description="Add a custom store configuration to get started"
			>
				<button
					onclick={handleOpenCreate}
					class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
				>
					<Plus size={16} />
					Add your first store
				</button>
			</EmptyState>
		{:else if filteredStores.length === 0}
			<EmptyState icon={StoreIcon} title="No {storeFilter} stores found" />
		{:else}
			<div class="grid md:grid-cols-2 lg:grid-cols-3 gap-4">
				{#each filteredStores as store (store.storeId)}
					<StoreCard {store} onTest={handleOpenTest} onEdit={handleOpenEdit} onDelete={handleOpenDelete} onExport={handleExport} />
				{/each}
			</div>
		{/if}
	</div>
</div>

<StoreModal
	isOpen={modalOpen}
	store={editingStore}
	onClose={handleCloseModal}
	onSave={handleSave}
/>

{#if testingStore}
	<TestStoreModal
		isOpen={testModalOpen}
		store={testingStore}
		onClose={handleCloseTest}
	/>
{/if}

<ConfirmModal
	isOpen={deleteModalOpen}
	title="Delete Store"
	message="Are you sure you want to delete this store configuration? This action cannot be undone."
	onConfirm={handleConfirmDelete}
	onCancel={handleCloseDelete}
	loading={deleteLoading}
/>

<ImportStoreModal
	isOpen={importModalOpen}
	onClose={() => (importModalOpen = false)}
	onImport={handleImport}
/>
