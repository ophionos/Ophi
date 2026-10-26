<script lang="ts">
	import type { PageData } from './$types';
	import { api, type ComparisonGroup } from '$lib/api/client';
	import ComparisonCard from '$lib/components/comparisons/ComparisonCard.svelte';
	import ComparisonModal from '$lib/components/comparisons/ComparisonModal.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import { GitCompare, Plus, Package } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';
	import PageHeader from '$lib/components/shared/PageHeader.svelte';
	import InlineStats from '$lib/components/shared/InlineStats.svelte';

	let { data }: { data: PageData } = $props();

	let modalOpen = $state(false);
	let deleteModalOpen = $state(false);
	let deletingGroup = $state<ComparisonGroup | undefined>(undefined);
	let deleteLoading = $state(false);

	// Rendered straight from the load data so the server HTML already has the rows. A writable
	// $derived: local edits apply at once, and a new load (navigation, invalidate) replaces them.
	let groups = $derived(data.groups);

	function handleOpenCreate() {
		modalOpen = true;
	}

	function handleCloseModal() {
		modalOpen = false;
	}

	async function handleSave(name: string, description?: string) {
		try {
			const created = await api.createComparisonGroup(name, description);
			groups = [...groups, created];
			toast.success(`Comparison group "${created.name}" created`);
			handleCloseModal();
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to create comparison group');
		}
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
			const deletedId = deletingGroup.id;
			groups = groups.filter((g) => g.id !== deletedId);
			toast.success(`Comparison group "${deletingGroup.name}" deleted`);
			handleCloseDelete();
		} catch (error) {
			console.error('Failed to delete comparison group:', error);
			toast.error('Failed to delete comparison group');
			deleteLoading = false;
		}
	}

	const stats = $derived({
		totalGroups: groups.length,
		totalProducts: groups.reduce((sum, g) => sum + g.productCount, 0)
	});
</script>

<svelte:head>
	<title>Comparison Groups - Ophi</title>
</svelte:head>

<div>
	<PageHeader title="Comparison Groups" description="Compare prices across similar products">
		{#snippet actions()}
			<button
				onclick={handleOpenCreate}
				class="whitespace-nowrap px-4 min-h-11 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
			>
				<Plus size={18} />
				Create Comparison
			</button>
		{/snippet}
	</PageHeader>

	<InlineStats
		stats={[
			{ label: 'Total Groups', value: stats.totalGroups, icon: GitCompare, testid: 'stat-total-groups' },
			{ label: 'Total Products', value: stats.totalProducts, icon: Package, testid: 'stat-total-products' }
		]}
	/>

	<div>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">All Comparison Groups</h2>
		{#if groups.length === 0}
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
				{#each groups as group (group.id)}
					<ComparisonCard
						{group}
						onDelete={handleOpenDelete}
						showLandedPrices={data.showLandedPrices ?? false}
					/>
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
