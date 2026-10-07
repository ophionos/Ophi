<script lang="ts">
	import { LoaderCircle } from 'lucide-svelte';
	import type { ComparisonGroupDetail } from '$lib/api/client';
	import { ApiError } from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		group?: ComparisonGroupDetail;
		onSubmit: (name: string, description?: string) => Promise<void>;
		onCancel: () => void;
	}

	let { group, onSubmit, onCancel }: Props = $props();

	const isEdit = $derived(!!group);

	let name = $state('');
	let description = $state('');
	let loading = $state(false);
	let error = $state('');

	$effect(() => {
		if (group) {
			name = group.name ?? '';
			description = group.description ?? '';
		} else {
			name = '';
			description = '';
		}
	});

	async function handleSubmit(e: Event) {
		e.preventDefault();

		if (!name.trim()) {
			error = 'Name is required';
			return;
		}

		loading = true;
		error = '';

		try {
			await onSubmit(name.trim(), description.trim() || undefined);
		} catch (err) {
			if (err instanceof ApiError) {
				error = err.message;
			} else {
				error = err instanceof Error ? err.message : 'An error occurred';
			}
		} finally {
			loading = false;
		}
	}
</script>

<form onsubmit={handleSubmit} class="space-y-4">
	{#if error}
		<FormError variant="box" message={error} />
	{/if}

	<div>
		<label for="name" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
			Name <span class="text-red-500">*</span>
		</label>
		<input
			id="name"
			type="text"
			bind:value={name}
			placeholder="Enter comparison group name"
			class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent"
			disabled={loading}
		/>
	</div>

	<div>
		<label for="description" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
			Description
		</label>
		<textarea
			id="description"
			bind:value={description}
			placeholder="Optional description"
			rows="3"
			class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent resize-none"
			disabled={loading}
		></textarea>
	</div>

	<div class="flex justify-end gap-3 pt-4">
		<button
			type="button"
			onclick={onCancel}
			disabled={loading}
			class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
		>
			Cancel
		</button>
		<button
			type="submit"
			disabled={loading}
			class="px-4 py-2 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 flex items-center gap-2"
		>
			{#if loading}
				<LoaderCircle size={16} class="animate-spin" />
			{/if}
			{isEdit ? 'Update' : 'Create'}
		</button>
	</div>
</form>
