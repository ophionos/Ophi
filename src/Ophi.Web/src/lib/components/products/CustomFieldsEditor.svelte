<script lang="ts">
	import { Plus, Trash2 } from 'lucide-svelte';
	import type { CustomField } from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		fields: CustomField[];
		onChange: (fields: CustomField[]) => void;
	}

	let { fields, onChange }: Props = $props();

	let duplicateError = $state('');

	function addField() {
		onChange([...fields, { name: '', value: '' }]);
	}

	function removeField(index: number) {
		const updated = fields.filter((_, i) => i !== index);
		onChange(updated);
		validateDuplicates(updated);
	}

	function updateField(index: number, key: 'name' | 'value', val: string) {
		const updated = fields.map((f, i) => (i === index ? { ...f, [key]: val } : f));
		onChange(updated);
		if (key === 'name') {
			validateDuplicates(updated);
		}
	}

	function validateDuplicates(list: CustomField[]) {
		const names = list.map((f) => f.name.trim().toLowerCase()).filter((n) => n !== '');
		const hasDuplicates = new Set(names).size !== names.length;
		duplicateError = hasDuplicates ? 'Duplicate field names are not allowed' : '';
	}
</script>

<div class="space-y-2">
	{#each fields as field, index (index)}
		<div class="flex gap-2 items-start">
			<input
				type="text"
				value={field.name}
				oninput={(e) => updateField(index, 'name', e.currentTarget.value)}
				placeholder="Field name"
				class="flex-1 px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
			/>
			<input
				type="text"
				value={field.value}
				oninput={(e) => updateField(index, 'value', e.currentTarget.value)}
				placeholder="Value"
				class="flex-1 px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
			/>
			<button
				type="button"
				onclick={() => removeField(index)}
				class="p-2 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-md"
				aria-label="Remove field"
			>
				<Trash2 size={16} />
			</button>
		</div>
	{/each}

	{#if duplicateError}
		<FormError message={duplicateError} />
	{/if}

	<button
		type="button"
		onclick={addField}
		class="flex items-center gap-1 text-sm text-brand dark:text-brand-muted hover:text-brand-hover dark:hover:text-brand-muted"
	>
		<Plus size={16} />
		Add Field
	</button>
</div>
