<script lang="ts">
	import Modal from '$lib/components/shared/Modal.svelte';
	import ComparisonForm from './ComparisonForm.svelte';
	import type { ComparisonGroupDetail } from '$lib/api/client';

	interface Props {
		isOpen: boolean;
		group?: ComparisonGroupDetail;
		onClose: () => void;
		onSave: (name: string, description?: string) => Promise<void>;
	}

	let { isOpen, group, onClose, onSave }: Props = $props();

	const isEdit = $derived(!!group);
	const title = $derived(isEdit ? 'Edit Comparison Group' : 'Create Comparison Group');

	async function handleSubmit(name: string, description?: string) {
		await onSave(name, description);
	}
</script>

<Modal {isOpen} {title} size="md" {onClose}>
	<div class="p-6">
		<ComparisonForm {group} onSubmit={handleSubmit} onCancel={onClose} />
	</div>
</Modal>
