<script lang="ts">
	import type { PageData } from './$types';
	import { api, type Tag } from '$lib/api/client';
	import { tags } from '$lib/stores/tags.svelte';
	import TagList from '$lib/components/tags/TagList.svelte';
	import TagModal from '$lib/components/tags/TagModal.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import { Tag as TagIcon, Plus } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	let { data }: { data: PageData } = $props();

	let modalOpen = $state(false);
	let editingTag = $state<Tag | undefined>(undefined);
	let deleteModalOpen = $state(false);
	let deletingTag = $state<Tag | undefined>(undefined);
	let deleteLoading = $state(false);

	$effect(() => {
		tags.set(data.tags);
	});

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
			tags.update(editingTag.id, updated);
			toast.success(`Tag "${updated.name}" updated`);
		} else {
			const created = await api.createTag(data.name, data.color, data.weight);
			tags.add({ ...created, productCount: 0 });
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
			tags.remove(deletingTag.id);
			toast.success(`Tag "${deletingTag.name}" deleted`);
			handleCloseDelete();
		} catch (error) {
			console.error('Failed to delete tag:', error);
			toast.error('Failed to delete tag');
			deleteLoading = false;
		}
	}

	const stats = $derived({
		total: tags.items.length,
		totalProducts: tags.items.reduce((sum, t) => sum + t.productCount, 0)
	});
</script>

<svelte:head>
	<title>Tags - Ophi</title>
</svelte:head>

<div>
	<div class="mb-8 flex items-start justify-between gap-4">
		<div>
			<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">Tags</h1>
			<p class="text-gray-600 dark:text-gray-400">
				Create and manage tags to organize your tracked products
			</p>
		</div>
		<button
			onclick={handleOpenCreate}
			class="shrink-0 whitespace-nowrap px-4 py-2 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
			data-testid="create-tag-button"
		>
			<Plus size={18} />
			Create Tag
		</button>
	</div>

	<!-- Slim inline summary, same as Comparisons — a single digit per stat doesn't warrant cards. -->
	<div class="flex flex-wrap items-center gap-x-4 gap-y-2 mb-6 px-1">
		<div class="flex items-center gap-2" data-testid="stat-total-tags">
			<TagIcon size={16} class="text-brand dark:text-brand-muted" />
			<span class="text-sm text-gray-500 dark:text-gray-400">Total Tags</span>
			<span class="text-sm font-semibold text-gray-900 dark:text-white">{stats.total}</span>
		</div>
		<div class="hidden sm:block w-px h-4 bg-gray-200 dark:bg-gray-700"></div>
		<div class="flex items-center gap-2" data-testid="stat-tagged-products">
			<TagIcon size={16} class="text-green-600 dark:text-green-400" />
			<span class="text-sm text-gray-500 dark:text-gray-400">Tagged Products</span>
			<span class="text-sm font-semibold text-gray-900 dark:text-white">{stats.totalProducts}</span>
		</div>
	</div>

	<div>
		<h2 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">All Tags</h2>
		{#if tags.items.length === 0}
			<EmptyState
				icon={TagIcon}
				title="No tags yet"
				description="Create your first tag to organize your products."
			>
				<button
					onclick={handleOpenCreate}
					class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
				>
					<Plus size={16} />
					Create your first tag
				</button>
			</EmptyState>
		{:else}
			<TagList tags={tags.items} onEdit={handleOpenEdit} onDelete={handleOpenDelete} />
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
