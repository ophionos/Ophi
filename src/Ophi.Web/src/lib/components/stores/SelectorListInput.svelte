<script lang="ts">
	import { Plus, X } from 'lucide-svelte';

	interface Props {
		label: string;
		values: string[];
		onChange: (values: string[]) => void;
		placeholder?: string;
		required?: boolean;
		helpText?: string;
	}

	let {
		label,
		values,
		onChange,
		placeholder = '',
		required = false,
		helpText = ''
	}: Props = $props();

	function addItem() {
		onChange([...values, '']);
	}

	function removeItem(index: number) {
		if (required && values.length <= 1) return;
		onChange(values.filter((_, i) => i !== index));
	}

	function updateItem(index: number, value: string) {
		const newValues = [...values];
		newValues[index] = value;
		onChange(newValues);
	}
</script>

<div class="space-y-2">
	<div class="flex items-center justify-between">
		<span class="block text-sm font-medium text-gray-700 dark:text-gray-300">
			{label}
			{#if required}
				<span class="text-red-500">*</span>
			{/if}
		</span>
		<button
			type="button"
			onclick={addItem}
			class="text-sm text-brand dark:text-brand-muted hover:text-brand dark:hover:text-brand-muted flex items-center gap-1"
		>
			<Plus size={14} />
			Add
		</button>
	</div>

	{#if helpText}
		<p class="text-xs text-gray-500 dark:text-gray-400">{helpText}</p>
	{/if}

	<div class="space-y-2">
		{#each values as value, index (index)}
			<div class="flex gap-2">
				<input
					type="text"
					{value}
					oninput={(e) => updateItem(index, e.currentTarget.value)}
					{placeholder}
					class="flex-1 px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent"
				/>
				<button
					type="button"
					onclick={() => removeItem(index)}
					disabled={required && values.length <= 1}
					class="p-2 text-gray-400 hover:text-red-500 dark:text-gray-500 dark:hover:text-red-400 disabled:opacity-30 disabled:cursor-not-allowed"
					title="Remove"
				>
					<X size={16} />
				</button>
			</div>
		{/each}
	</div>

	{#if values.length === 0}
		<p class="text-sm text-gray-400 dark:text-gray-500 italic">No items added</p>
	{/if}
</div>
