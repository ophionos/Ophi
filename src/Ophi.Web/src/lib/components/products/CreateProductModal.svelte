<script lang="ts">
	import Modal from '$lib/components/shared/Modal.svelte';
	import { isValidHttpUrl } from '$lib/format';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		onClose: () => void;
		onSave: (data: { name: string; imageUrl?: string; currency?: string }) => Promise<void>;
	}

	let { isOpen, onClose, onSave }: Props = $props();

	let name = $state('');
	let imageUrl = $state('');
	let currency = $state('USD');
	let saving = $state(false);
	let error = $state('');
	let nameError = $state('');
	let imageUrlError = $state('');

	$effect(() => {
		if (isOpen) {
			name = '';
			imageUrl = '';
			currency = 'USD';
			error = '';
			nameError = '';
			imageUrlError = '';
			saving = false;
		}
	});

	function validate(): boolean {
		nameError = '';
		imageUrlError = '';

		let valid = true;

		if (!name.trim()) {
			nameError = 'Name is required';
			valid = false;
		} else if (name.length > 500) {
			nameError = 'Name must not exceed 500 characters';
			valid = false;
		}

		if (imageUrl.trim() && !isValidHttpUrl(imageUrl.trim())) {
			imageUrlError = 'Must be a valid HTTP or HTTPS URL';
			valid = false;
		}

		return valid;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!validate()) return;

		saving = true;
		error = '';

		try {
			await onSave({
				name: name.trim(),
				imageUrl: imageUrl.trim() || undefined,
				currency: currency || undefined
			});
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to create product';
		} finally {
			saving = false;
		}
	}
</script>

<Modal {isOpen} title="Create Product from Scratch" size="md" titleId="create-product-modal-title" {onClose}>
	<form onsubmit={handleSubmit} class="flex-1 overflow-y-auto overscroll-contain p-6 space-y-4">
		{#if error}
			<FormError variant="box" testid="create-product-error" message={error} />
		{/if}

		<!-- Name -->
		<div>
			<label
				for="product-name"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
			>
				Product Name <span class="text-red-500">*</span>
			</label>
			<input
				id="product-name"
				type="text"
				bind:value={name}
				placeholder="e.g., iPhone 16 Pro"
				class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				data-testid="create-product-name"
				aria-invalid={nameError ? true : undefined}
				aria-describedby={nameError ? 'create-product-name-error' : undefined}
			/>
			{#if nameError}
				<FormError class="mt-1" id="create-product-name-error" testid="name-error" message={nameError} />
			{/if}
		</div>

		<!-- Image URL -->
		<div>
			<label
				for="product-image"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
			>
				Image URL <span class="text-gray-400">(optional)</span>
			</label>
			<input
				id="product-image"
				type="text"
				bind:value={imageUrl}
				placeholder="https://example.com/image.jpg"
				class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				data-testid="create-product-image-url"
				aria-invalid={imageUrlError ? true : undefined}
				aria-describedby={imageUrlError ? 'create-product-image-error' : undefined}
			/>
			{#if imageUrlError}
				<FormError
					class="mt-1"
					id="create-product-image-error"
					testid="image-url-error"
					message={imageUrlError}
				/>
			{/if}
		</div>

		<!-- Currency -->
		<div>
			<label
				for="product-currency"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
			>
				Currency
			</label>
			<select
				id="product-currency"
				bind:value={currency}
				class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				data-testid="create-product-currency"
			>
				<option value="USD">USD - US Dollar</option>
				<option value="EUR">EUR - Euro</option>
				<option value="GBP">GBP - British Pound</option>
				<option value="CAD">CAD - Canadian Dollar</option>
				<option value="AUD">AUD - Australian Dollar</option>
				<option value="JPY">JPY - Japanese Yen</option>
			</select>
		</div>

		<p class="text-xs text-gray-500 dark:text-gray-400">
			You can add store URLs from the product detail page after creation.
		</p>

		<!-- Actions -->
		<div class="flex justify-end gap-3 pt-4 border-t border-gray-200 dark:border-gray-700">
			<button
				type="button"
				onclick={onClose}
				class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600"
			>
				Cancel
			</button>
			<button
				type="submit"
				disabled={saving}
				class="px-4 py-2 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50"
				data-testid="create-product-submit"
			>
				{saving ? 'Creating...' : 'Create Product'}
			</button>
		</div>
	</form>
</Modal>
