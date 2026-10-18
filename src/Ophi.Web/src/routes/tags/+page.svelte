<script lang="ts">
	import type { PageData } from './$types';
	import { api, type Tag } from '$lib/api/client';
	import TagList from '$lib/components/tags/TagList.svelte';
	import TagModal from '$lib/components/tags/TagModal.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import { Tag as TagIcon, Plus, Package } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';
	import PageHeader from '$lib/components/shared/PageHeader.svelte';
	import InlineStats from '$lib/components/shared/InlineStats.svelte';

	let { data }: { data: PageData } = $props();

	let modalOpen = $state(false);
	let editingTag = $state<Tag | undefined>(undefined);
	let deleteModalOpen = $state(false);
	let deletingTag = $state<Tag | undefined>(undefined);
	let deleteLoading = $state(false);

	// Rendered straight from the load data so the server HTML already has the rows (writable
	// $derived: local edits apply at once, a new load replaces them).
	let tags = $derived(data.tags);

	function handleOpenCreate() {
		editingTag = undefined;
		modalOpen = true;
	}

	function handleOpenEdit(tag: Tag) {
		editingTag = tag;
		modalOpen = true;
	}

	function handleCloseModal() {
		modalOpen = false;
		editingTag = undefined;
	}

	async function handleSave(data: { name: string; color: string; weight: number }) {
		if (editingTag) {
			const updated = await api.updateTag(editingTag.id, data);
			const editedId = editingTag.id;
			tags = tags.map((t) => (t.id === editedId ? { ...t, ...updated } : t));
			toast.success(`Tag "${updated.name}" updated`);
		} else {
			const created = await api.createTag(data.name, data.color, data.weight);
			tags = [...tags, { ...created, productCount: 0 }];
			toast.success(`Tag "${created.name}" created`);
		}
		handleCloseModal();
	}

	function handleOpenDelete(tag: Tag) {
		deletingTag = tag;
		deleteModalOpen = true;
	}

	function handleCloseDelete() {
		deleteModalOpen = false;
		deletingTag = undefined;
		deleteLoading = false;
	}

	async function handleConfirmDelete() {
		if (!deletingTag) return;

		deleteLoading = true;
		try {
			await api.deleteTag(deletingTag.id);
			const deletedId = deletingTag.id;
			tags = tags.filter((t) => t.id !== deletedId);
			toast.success(`Tag "${deletingTag.name}" deleted`);
			handleCloseDelete();
		} catch (error) {
			console.error('Failed to delete tag:', error);
			toast.error('Failed to delete tag');
			deleteLoading = false;
		}
	}

	const stats = $derived({
		total: tags.length,
		totalProducts: tags.reduce((sum, t) => sum + t.productCount, 0)
	});
</script>

<svelte:head>
	<title>Tags - Ophi</title>
</svelte:head>

<div>
	<PageHeader title="Tags" description="Create and manage tags to organize your tracked products">
		{#snippet actions()}
			<button
				onclick={handleOpenCreate}
				class="whitespace-nowrap px-4 min-h-11 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
				data-testid="create-tag-button"
			>
				<Plus size={18} />
				Create Tag
			</button>
		{/snippet}
	</PageHeader>

	<InlineStats
		stats={[
			{ label: 'Total Tags', value: stats.total, icon: TagIcon, testid: 'stat-total-tags' },
			{ label: 'Tagged Products', value: stats.totalProducts, icon: Package, testid: 'stat-tagged-products' }
		]}
	/>

	<div>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">All Tags</h2>
		{#if tags.length === 0}
			<EmptyState
				icon={TagIcon}
				title="No tags yet"
				description="Create your first tag to organize your products."
			>
				<button
					onclick={handleOpenCreate}
					class="inline-flex items-center gap-2 px-4 min-h-11 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
				>
					<Plus size={16} />
					Create your first tag
				</button>
			</EmptyState>
		{:else}
			<TagList {tags} onEdit={handleOpenEdit} onDelete={handleOpenDelete} />
		{/if}
	</div>
</div>

<TagModal isOpen={modalOpen} tag={editingTag} onClose={handleCloseModal} onSave={handleSave} />

<ConfirmModal
	isOpen={deleteModalOpen}
	title="Delete Tag"
	message="Are you sure you want to delete this tag? Products will be untagged but not deleted."
	onConfirm={handleConfirmDelete}
	onCancel={handleCloseDelete}
	loading={deleteLoading}
/>
