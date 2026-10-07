<script lang="ts">
	import type { PageData } from './$types';
	import { api, type ComparisonGroup } from '$lib/api/client';
	import { comparisons } from '$lib/stores/comparisons.svelte';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import ComparisonCard from '$lib/components/comparisons/ComparisonCard.svelte';
	import ComparisonModal from '$lib/components/comparisons/ComparisonModal.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import { GitCompare, Plus, Package } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	let { data }: { data: PageData } = $props();

	let modalOpen = $state(false);
	let deleteModalOpen = $state(false);
	let deletingGroup = $state<ComparisonGroup | undefined>(undefined);
	let deleteLoading = $state(false);

	$effect(() => {
		comparisons.set(data.groups);
	});

	function handleOpenCreate() {
		modalOpen = true;
	}

	function handleCloseModal() {
		modalOpen = false;
	}

	async function handleSave(name: string, description?: string) {
		try {
			const created = await api.createComparisonGroup(name, description);
			comparisons.add(created);
			toast.success(`Comparison group "${created.name}" created`);
			handleCloseModal();
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to create comparison group');
		}
	}

	function handleView(group: ComparisonGroup) {
		goto(resolve(`/comparisons/${group.id}`));
	}

	function handleOpenDelete(group: ComparisonGroup) {
		deletingGroup = group;
		deleteModalOpen = true;
	}

	function handleCloseDelete() {
		deleteModalOpen = false;
		deletingGroup = undefined;
		deleteLoading = false;
	}

	async function handleConfirmDelete() {
		if (!deletingGroup?.id) return;

		deleteLoading = true;
		try {
			await api.deleteComparisonGroup(deletingGroup.id);
			comparisons.remove(deletingGroup.id);
			toast.success(`Comparison group "${deletingGroup.name}" deleted`);
			handleCloseDelete();
		} catch (error) {
			console.error('Failed to delete comparison group:', error);
			toast.error('Failed to delete comparison group');
			deleteLoading = false;
		}
	}

	const stats = $derived({
		totalGroups: comparisons.items.length,
		totalProducts: comparisons.items.reduce((sum, g) => sum + g.productCount, 0)
	});
</script>

<svelte:head>
	<title>Comparison Groups - Ophi</title>
</svelte:head>

<div>
	<div class="mb-8 flex items-start justify-between">
		<div>
			<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">Comparison Groups</h1>
			<p class="text-gray-600 dark:text-gray-400">Compare prices across similar products</p>
		</div>
		<button
			onclick={handleOpenCreate}
			class="px-4 py-2 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
		>
			<Plus size={18} />
			Create Comparison
		</button>
	</div>

	<!-- Slim inline summary — a single digit per stat doesn't warrant full-width cards. -->
	<div class="flex flex-wrap items-center gap-x-4 gap-y-2 mb-6 px-1">
		<div class="flex items-center gap-2" data-testid="stat-total-groups">
			<GitCompare size={16} class="text-brand dark:text-brand-muted" />
			<span class="text-sm text-gray-500 dark:text-gray-400">Total Groups</span>
			<span class="text-sm font-semibold text-gray-900 dark:text-white">{stats.totalGroups}</span>
		</div>
		<div class="hidden sm:block w-px h-4 bg-gray-200 dark:bg-gray-700"></div>
		<div class="flex items-center gap-2" data-testid="stat-total-products">
			<Package size={16} class="text-green-600 dark:text-green-400" />
			<span class="text-sm text-gray-500 dark:text-gray-400">Total Products</span>
			<span class="text-sm font-semibold text-gray-900 dark:text-white">{stats.totalProducts}</span>
		</div>
	</div>

	<div>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">All Comparison Groups</h2>
		{#if comparisons.items.length === 0}
			<EmptyState
				icon={GitCompare}
				title="No comparison groups yet"
				description="Create a comparison group to compare prices across products"
			>
				<button
					onclick={handleOpenCreate}
					class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
				>
					<Plus size={16} />
					Create your first comparison
				</button>
			</EmptyState>
		{:else}
			<div class="grid md:grid-cols-2 lg:grid-cols-3 gap-4">
				{#each comparisons.items as group (group.id)}
					<ComparisonCard {group} onView={handleView} onDelete={handleOpenDelete} />
				{/each}
			</div>
		{/if}
	</div>
</div>

<ComparisonModal isOpen={modalOpen} onClose={handleCloseModal} onSave={handleSave} />

<ConfirmModal
	isOpen={deleteModalOpen}
	title="Delete Comparison Group"
	message="Are you sure you want to delete this comparison group? This action cannot be undone."
	onConfirm={handleConfirmDelete}
	onCancel={handleCloseDelete}
	loading={deleteLoading}
/>
