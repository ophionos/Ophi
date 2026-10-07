<script lang="ts">
	import { Plus, LoaderCircle, RefreshCw } from 'lucide-svelte';
	import { ApiError } from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		onSubmit: (url: string) => Promise<void>;
	}

	let { onSubmit }: Props = $props();

	let url = $state('');
	let loading = $state(false);
	let error = $state('');
	let isNetworkError = $state(false);

	function validateUrl(value: string): string {
		if (!value.trim()) return '';
		try {
			const parsed = new URL(value);
			if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
				return 'URL must start with http:// or https://';
			}
			return '';
		} catch {
			return 'Please enter a valid URL';
		}
	}

	function handleBlur() {
		if (url.trim()) {
			error = validateUrl(url);
		}
	}

	function clearError() {
		if (error) {
			error = '';
			isNetworkError = false;
		}
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!url.trim()) return;

		loading = true;
		error = '';
		isNetworkError = false;

		try {
			await onSubmit(url);
			url = '';
		} catch (err) {
			if (err instanceof ApiError) {
				if (err.hasFieldErrors()) {
					error = err.getFieldErrors('url')[0] ?? err.message;
				} else if (err.code === 'UnknownError') {
					error = 'Something went wrong. Please try again.';
					isNetworkError = true;
				} else {
					error = err.message;
				}
			} else {
				error = err instanceof Error ? err.message : 'Failed to add product';
				isNetworkError = true;
			}
		} finally {
			loading = false;
		}
	}

	async function retry() {
		await handleSubmit(new Event('submit'));
	}
</script>

<form onsubmit={handleSubmit} class="flex gap-2">
	<div class="flex-1">
		<input
			type="url"
			bind:value={url}
			onblur={handleBlur}
			oninput={clearError}
			placeholder="Paste product URL..."
			class="w-full px-4 py-2.5 border rounded-xl focus:ring-4 focus:ring-brand/20 focus:border-brand transition-all duration-300 outline-none bg-white dark:bg-gray-700/50 dark:focus:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 shadow-sm focus:shadow-md {error ? 'border-red-500 focus:ring-red-500/20 focus:border-red-500' : 'border-gray-300 dark:border-gray-600'}"
			disabled={loading}
			required
			aria-invalid={error ? true : undefined}
			aria-describedby={error ? 'add-product-error' : undefined}
		/>
		{#if error}
			<FormError class="mt-1" id="add-product-error" message={error}>
				{#if isNetworkError}
					<button
						type="button"
						onclick={retry}
						disabled={loading}
						class="text-sm text-brand dark:text-brand-muted hover:underline flex items-center gap-1"
						aria-label="Retry adding product"
					>
						<RefreshCw size={14} />
						Retry
					</button>
				{/if}
			</FormError>
		{/if}
	</div>
	<button
		type="submit"
		disabled={loading}
		class="px-4 py-2 bg-brand text-white rounded-lg hover:bg-brand-hover disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
	>
		{#if loading}
			<LoaderCircle size={18} class="animate-spin" />
		{:else}
			<Plus size={18} />
		{/if}
		Add
	</button>
</form>
