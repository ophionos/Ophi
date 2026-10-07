<script lang="ts">
	import Modal from '$lib/components/shared/Modal.svelte';
	import type { Tag } from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		tag?: Tag | null;
		onClose: () => void;
		onSave: (data: { name: string; color: string; weight: number }) => Promise<void>;
	}

	let { isOpen, tag = null, onClose, onSave }: Props = $props();

	let name = $state('');
	let color = $state('#3B82F6');
	let weight = $state(0);
	let saving = $state(false);
	let error = $state('');
	let nameError = $state('');

	const isEditing = $derived(!!tag);
	const title = $derived(isEditing ? 'Edit Tag' : 'Create Tag');

	const presetColors = [
		'#EF4444', // Red
		'#F97316', // Orange
		'#EAB308', // Yellow
		'#22C55E', // Green
		'#14B8A6', // Teal
		'#3B82F6', // Blue
		'#8B5CF6', // Purple
		'#EC4899', // Pink
		'#6B7280', // Gray
		'#000000' // Black
	];

	$effect(() => {
		if (isOpen) {
			if (tag) {
				name = tag.name;
				color = tag.color;
				weight = tag.weight;
			} else {
				name = '';
				color = '#3B82F6';
				weight = 0;
			}
			error = '';
			nameError = '';
			saving = false;
		}
	});

	function validate(): boolean {
		nameError = '';

		if (!name.trim()) {
			nameError = 'Name is required';
			return false;
		}

		if (name.length > 50) {
			nameError = 'Name must not exceed 50 characters';
			return false;
		}

		return true;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!validate()) return;

		saving = true;
		error = '';

		try {
			await onSave({ name: name.trim(), color, weight });
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to save tag';
		} finally {
			saving = false;
		}
	}
</script>

<Modal {isOpen} {title} size="sm" position="top" {onClose}>
	<form onsubmit={handleSubmit} class="flex-1 overflow-y-auto p-6 space-y-4">
		{#if error}
			<FormError variant="box" message={error} />
		{/if}

		<!-- Name -->
		<div>
			<label
				for="tag-name"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
			>
				Name
			</label>
			<input
				id="tag-name"
				type="text"
				bind:value={name}
				placeholder="e.g., Electronics, Wishlist"
				class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				data-testid="tag-name-input"
				aria-invalid={nameError ? true : undefined}
				aria-describedby={nameError ? 'tag-name-error' : undefined}
			/>
			{#if nameError}
				<FormError class="mt-1" id="tag-name-error" message={nameError} />
			{/if}
		</div>

		<!-- Color -->
		<div>
			<span class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
				Color
			</span>
			<div class="flex flex-wrap gap-2">
				{#each presetColors as presetColor (presetColor)}
					<button
						type="button"
						onclick={() => (color = presetColor)}
						class="w-8 h-8 rounded-full border-2 transition-transform hover:scale-110 {color ===
						presetColor
							? 'border-gray-900 dark:border-white ring-2 ring-offset-2 ring-brand'
							: 'border-transparent'}"
						style="background-color: {presetColor}"
						aria-label="Select color {presetColor}"
					></button>
				{/each}
			</div>
			<div class="mt-2 flex items-center gap-2">
				<input
					type="color"
					bind:value={color}
					class="w-8 h-8 rounded cursor-pointer"
					title="Custom color"
				/>
				<span class="text-sm text-gray-500 dark:text-gray-400">{color}</span>
			</div>
		</div>

		<!-- Weight (sort order) -->
		<div>
			<label
				for="tag-weight"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
			>
				Sort Order
			</label>
			<input
				id="tag-weight"
				type="number"
				bind:value={weight}
				class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				data-testid="tag-weight-input"
			/>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Higher numbers appear first
			</p>
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
				data-testid="tag-save-button"
			>
				{saving ? 'Saving...' : isEditing ? 'Save Changes' : 'Create Tag'}
			</button>
		</div>
	</form>
</Modal>
