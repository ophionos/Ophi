<script lang="ts">
	import AlertForm from './AlertForm.svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import type { AlertCondition } from '$lib/api/client';

	interface ExistingAlert {
		condition: AlertCondition;
		active: boolean;
	}

	interface Props {
		isOpen: boolean;
		productName: string;
		currentPrice?: number;
		currency: string;
		existingAlerts?: ExistingAlert[];
		onClose: () => void;
		onSave: (targetPrice: number, condition: AlertCondition) => Promise<void>;
	}

	let { isOpen, productName, currentPrice, currency, existingAlerts = [], onClose, onSave }: Props = $props();

	async function handleSubmit(targetPrice: number, condition: AlertCondition) {
		await onSave(targetPrice, condition);
	}
</script>

<Modal {isOpen} title="Create Price Alert" size="md" titleId="alert-modal-title" {onClose}>
	<div class="flex-1 overflow-y-auto overscroll-contain p-6">
		<AlertForm
			{productName}
			{currentPrice}
			{currency}
			{existingAlerts}
			onSubmit={handleSubmit}
			onCancel={onClose}
		/>
	</div>
</Modal>
