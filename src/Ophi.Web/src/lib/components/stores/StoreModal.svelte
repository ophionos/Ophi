<script lang="ts">
	import StoreForm from './StoreForm.svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import type { Store, CreateStoreRequest, UpdateStoreRequest } from '$lib/api/client';

	interface Props {
		isOpen: boolean;
		store?: Store;
		onClose: () => void;
		onSave: (data: CreateStoreRequest | UpdateStoreRequest) => Promise<void>;
	}

	let { isOpen, store, onClose, onSave }: Props = $props();

	const isEdit = $derived(!!store);
	const title = $derived(isEdit ? 'Edit Store Configuration' : 'Create Store Configuration');

	async function handleSubmit(data: CreateStoreRequest | UpdateStoreRequest) {
		await onSave(data);
	}
</script>

<Modal {isOpen} {title} size="2xl" {onClose}>
	<div class="flex-1 overflow-y-auto p-6">
		<StoreForm {store} onSubmit={handleSubmit} onCancel={onClose} />
	</div>
</Modal>
