<script lang="ts">
	import { LoaderCircle } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import { isValidHttpUrl } from '$lib/format';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		onClose: () => void;
		onSave: (url: string) => Promise<void>;
	}

	let { isOpen, onClose, onSave }: Props = $props();

	let url = $state('');
	let loading = $state(false);
	let error = $state('');

	$effect(() => {
		if (isOpen) {
			url = '';
			loading = false;
			error = '';
		}
	});

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!url.trim()) return;

		loading = true;
		error = '';

		try {
			await onSave(url.trim());
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'Failed to add URL';
			loading = false;
		}
	}

	function handleBlur() {
		if (url.trim() && !isValidHttpUrl(url.trim())) {
			error = 'Please enter a valid URL (http:// or https://)';
		}
	}
</script>

<Modal {isOpen} title="Add Store URL" size="md" position="center" {onClose}>
	<form onsubmit={handleSubmit} class="p-4 space-y-4">
		<div>
			<label for="url-input" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
				Product URL
			</label>
			<input
				id="url-input"
				type="url"
				bind:value={url}
				onblur={handleBlur}
				oninput={() => { if (error) error = ''; }}
				placeholder="https://store.com/product"
				required
				disabled={loading}
				class="w-full px-3 py-2 border rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 focus:ring-2 focus:ring-brand focus:border-transparent disabled:opacity-50 {error ? 'border-red-500' : 'border-gray-300 dark:border-gray-600'}"
			/>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Add another store URL to compare prices
			</p>
		</div>

		{#if error}
			<FormError message={error} />
		{/if}

		<div class="flex justify-end gap-3">
			<button
				type="button"
				onclick={onClose}
				disabled={loading}
				class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600 disabled:opacity-50"
			>
				Cancel
			</button>
			<button
				type="submit"
				disabled={loading || !url.trim() || !isValidHttpUrl(url)}
				class="px-4 py-2 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 flex items-center gap-2"
			>
				{#if loading}
					<LoaderCircle size={16} class="animate-spin" />
				{/if}
				Add URL
			</button>
		</div>
	</form>
</Modal>
