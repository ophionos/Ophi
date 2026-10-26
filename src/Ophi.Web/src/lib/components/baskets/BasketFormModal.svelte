<script lang="ts">
	import { LoaderCircle } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		open: boolean;
		/** Set to rename an existing basket. */
		basket?: { name: string; description?: string | null };
		onClose: () => void;
		/** Throw to show the message under the name field. */
		onSave: (name: string, description?: string) => Promise<void>;
	}

	let { open, basket, onClose, onSave }: Props = $props();

	let name = $state('');
	let description = $state('');
	let saving = $state(false);
	let error = $state('');

	$effect(() => {
		if (!open) return;
		name = basket?.name ?? '';
		description = basket?.description ?? '';
		error = '';
	});

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!name.trim()) {
			error = 'Name is required';
			return;
		}
		saving = true;
		error = '';
		try {
			await onSave(name.trim(), description.trim() || undefined);
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to save basket';
		} finally {
			saving = false;
		}
	}
</script>

<Modal isOpen={open} title={basket ? 'Rename basket' : 'New basket'} size="md" {onClose}>
	<form onsubmit={handleSubmit} class="space-y-4 p-6">
		<div>
			<label for="basket-name" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Name <span class="text-danger">*</span>
			</label>
			<input
				id="basket-name"
				type="text"
				bind:value={name}
				disabled={saving}
				aria-describedby={error ? 'basket-name-error' : undefined}
				class="mt-1 w-full px-3 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md bg-surface-field text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent"
			/>
			{#if error}
				<FormError id="basket-name-error" message={error} />
			{/if}
		</div>
		<div>
			<label
				for="basket-description"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300">Description</label
			>
			<textarea
				id="basket-description"
				bind:value={description}
				rows="3"
				disabled={saving}
				class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-surface-field text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent resize-none"
			></textarea>
		</div>
		<div class="flex justify-end gap-3 pt-2">
			<button
				type="button"
				onclick={onClose}
				disabled={saving}
				class="px-4 min-h-11 text-sm font-medium text-gray-700 dark:text-gray-300 bg-surface-field border border-gray-300 dark:border-gray-600 rounded-md hover:bg-surface-field-hover disabled:opacity-50"
			>
				Cancel
			</button>
			<button
				type="submit"
				disabled={saving}
				class="flex items-center gap-2 px-4 min-h-11 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50"
			>
				{#if saving}
					<LoaderCircle size={16} class="animate-spin" />
				{/if}
				{basket ? 'Save' : 'Create'}
			</button>
		</div>
	</form>
</Modal>
