<script lang="ts">
	import Modal from '$lib/components/shared/Modal.svelte';
	import type { ProductDetail, Tag, ProductTag, CustomField, ProductStatus } from '$lib/api/client';
	import { isValidHttpUrl, CHECK_INTERVAL_OPTIONS } from '$lib/format';
	import TagPicker from '$lib/components/tags/TagPicker.svelte';
	import CustomFieldsEditor from '$lib/components/products/CustomFieldsEditor.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		product: ProductDetail;
		availableTags?: Tag[];
		onClose: () => void;
		onSave: (data: {
			name?: string;
			imageUrl?: string;
			status?: ProductStatus;
			customFields?: CustomField[];
			checkIntervalMinutes?: number | null;
		}) => Promise<void>;
		onAddTag?: (tagId: string) => Promise<void>;
		onRemoveTag?: (tagId: string) => Promise<void>;
	}

	let { isOpen, product, availableTags = [], onClose, onSave, onAddTag, onRemoveTag }: Props =
		$props();

	let name = $state('');
	let imageUrl = $state('');
	let status = $state<ProductStatus>('active');
	let checkInterval = $state('');
	let productTags = $state<ProductTag[]>([]);
	let customFields = $state<CustomField[]>([]);
	let saving = $state(false);
	let error = $state('');
	let nameError = $state('');
	let imageUrlError = $state('');

	$effect(() => {
		if (isOpen) {
			name = product.name;
			imageUrl = product.imageUrl ?? '';
			status = product.status;
			checkInterval = product.checkIntervalMinutes?.toString() ?? '';
			productTags = [...(product.tags ?? [])];
			customFields = [...(product.customFields ?? [])].map((f) => ({ ...f }));
			error = '';
			nameError = '';
			imageUrlError = '';
			saving = false;
		}
	});

	async function handleAddTag(tagId: string) {
		if (!onAddTag) return;
		await onAddTag(tagId);
		productTags = [...(product.tags ?? [])];
	}

	async function handleRemoveTag(tagId: string) {
		if (!onRemoveTag) return;
		await onRemoveTag(tagId);
		productTags = productTags.filter((t) => t.id !== tagId);
	}

	function handleCustomFieldsChange(fields: CustomField[]) {
		customFields = fields;
	}

	function validate(): boolean {
		nameError = '';
		imageUrlError = '';

		if (name.length > 500) {
			nameError = 'Name must not exceed 500 characters';
			return false;
		}

		if (imageUrl) {
			if (imageUrl.length > 2048) {
				imageUrlError = 'Image URL must not exceed 2048 characters';
				return false;
			}
			if (!isValidHttpUrl(imageUrl)) {
				imageUrlError = 'Must be a valid HTTP or HTTPS URL';
				return false;
			}
		}

		return true;
	}

	function customFieldsChanged(): boolean {
		const original = product.customFields ?? [];
		if (customFields.length !== original.length) return true;
		return customFields.some(
			(f, i) => f.name !== original[i].name || f.value !== original[i].value
		);
	}

	function getIntervalValue(): number | null | undefined {
		const originalStr = product.checkIntervalMinutes?.toString() ?? '';
		if (checkInterval === originalStr) return undefined; // unchanged
		if (checkInterval === '') return 0; // reset to default (sends 0 to API)
		return parseInt(checkInterval, 10);
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!validate()) return;

		saving = true;
		error = '';

		try {
			const data: {
				name?: string;
				imageUrl?: string;
				status?: ProductStatus;
				customFields?: CustomField[];
				checkIntervalMinutes?: number | null;
			} = {};
			if (name !== product.name) data.name = name;
			if (imageUrl !== (product.imageUrl ?? '')) data.imageUrl = imageUrl;
			if (status !== product.status) data.status = status;
			if (customFieldsChanged()) {
				data.customFields = customFields.filter((f) => f.name.trim() !== '');
			}
			const intervalValue = getIntervalValue();
			if (intervalValue !== undefined) {
				data.checkIntervalMinutes = intervalValue;
			}

			if (Object.keys(data).length === 0) {
				onClose();
				return;
			}

			await onSave(data);
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to update product';
		} finally {
			saving = false;
		}
	}

	const canToggleStatus = $derived(product.status === 'active' || product.status === 'paused');
</script>

<Modal {isOpen} title="Edit Product" size="lg" {onClose}>
	<form onsubmit={handleSubmit} class="flex-1 overflow-y-auto p-6 space-y-4">
					{#if error}
						<FormError variant="box" message={error} />
					{/if}

					<!-- Name -->
					<div>
						<label
							for="product-name"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Name
						</label>
						<input
							id="product-name"
							type="text"
							bind:value={name}
							class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
							aria-invalid={nameError ? true : undefined}
							aria-describedby={nameError ? 'edit-product-name-error' : undefined}
						/>
						{#if nameError}
							<FormError class="mt-1" id="edit-product-name-error" message={nameError} />
						{/if}
					</div>

					<!-- Image URL -->
					<div>
						<label
							for="product-image-url"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Image URL
						</label>
						<input
							id="product-image-url"
							type="text"
							bind:value={imageUrl}
							placeholder="https://example.com/image.jpg"
							class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
							aria-invalid={imageUrlError ? true : undefined}
							aria-describedby={imageUrlError ? 'edit-product-image-error' : undefined}
						/>
						{#if imageUrlError}
							<FormError class="mt-1" id="edit-product-image-error" message={imageUrlError} />
						{/if}
					</div>

					<!-- Check Interval -->
					<div>
						<label
							for="check-interval"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Check Interval
						</label>
						<select
							id="check-interval"
							bind:value={checkInterval}
							class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
						>
							{#each CHECK_INTERVAL_OPTIONS as option (option.value)}
								<option value={option.value}>{option.label}</option>
							{/each}
						</select>
						<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
							How often to check this product for price changes
						</p>
					</div>

					<!-- Status Toggle -->
					{#if canToggleStatus}
						<div>
							<label
								for="product-status-toggle"
								class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
							>
								Status
							</label>
							<div class="flex items-center gap-3">
								<button
									id="product-status-toggle"
									type="button"
									aria-label="Toggle product status"
									onclick={() => (status = status === 'active' ? 'paused' : 'active')}
									class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors {status ===
									'active'
										? 'bg-green-500'
										: 'bg-gray-300 dark:bg-gray-600'}"
								>
									<span
										class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {status ===
										'active'
											? 'translate-x-6'
											: 'translate-x-1'}"
									></span>
								</button>
								<span class="text-sm text-gray-700 dark:text-gray-300 capitalize">{status}</span>
							</div>
						</div>
					{/if}

					<!-- Tags -->
					{#if onAddTag && onRemoveTag}
						<div>
							<span class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
								Tags
							</span>
							<TagPicker
								{availableTags}
								selectedTags={productTags}
								onAdd={handleAddTag}
								onRemove={handleRemoveTag}
							/>
						</div>
					{/if}

					<!-- Custom Fields -->
					<div>
						<span class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
							Custom Fields
						</span>
						<CustomFieldsEditor fields={customFields} onChange={handleCustomFieldsChange} />
					</div>

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
			>
				{saving ? 'Saving...' : 'Save Changes'}
			</button>
		</div>
	</form>
</Modal>
