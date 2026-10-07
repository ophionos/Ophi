<script lang="ts">
	import { LoaderCircle, Copy, Check } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		onClose: () => void;
		onSave: (data: { name: string; scopes: string[]; expiresAt?: string }) => Promise<void>;
		createdKey?: string;
	}

	let { isOpen, onClose, onSave, createdKey }: Props = $props();

	let name = $state('');
	let scopeRead = $state(true);
	let scopeWrite = $state(false);
	let expiresIn = $state('');
	let saving = $state(false);
	let error = $state('');
	let copied = $state(false);

	const title = $derived(createdKey ? 'API Key Created' : 'Create API Key');

	function resetForm() {
		name = '';
		scopeRead = true;
		scopeWrite = false;
		expiresIn = '';
		saving = false;
		error = '';
		copied = false;
	}

	function handleClose() {
		if (saving) return;
		resetForm();
		onClose();
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		error = '';

		const scopes: string[] = [];
		if (scopeRead) scopes.push('read');
		if (scopeWrite) scopes.push('write');

		if (!name.trim()) {
			error = 'Name is required';
			return;
		}
		if (scopes.length === 0) {
			error = 'Select at least one scope';
			return;
		}

		let expiresAt: string | undefined;
		if (expiresIn) {
			const days = parseInt(expiresIn, 10);
			const date = new Date();
			date.setDate(date.getDate() + days);
			expiresAt = date.toISOString();
		}

		saving = true;
		try {
			await onSave({ name: name.trim(), scopes, expiresAt });
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to create API key';
			saving = false;
		}
	}

	async function handleCopyKey() {
		if (!createdKey) return;
		await navigator.clipboard.writeText(createdKey);
		copied = true;
		setTimeout(() => (copied = false), 2000);
	}
</script>

<Modal
	{isOpen}
	{title}
	size="md"
	position="center"
	closeOnBackdrop={!saving}
	closeOnEscape={!saving}
	onClose={handleClose}
>
	<div class="p-6">
		{#if createdKey}
			<p class="text-sm text-amber-600 dark:text-amber-400 mb-3">
				Copy this key now — it will not be shown again.
			</p>
			<div class="flex items-center gap-2 bg-gray-50 dark:bg-gray-900 p-3 rounded-md border border-gray-200 dark:border-gray-700">
				<code class="flex-1 text-sm font-mono text-gray-900 dark:text-gray-100 break-all select-all">{createdKey}</code>
				<button
					onclick={handleCopyKey}
					class="shrink-0 p-1.5 text-gray-500 hover:text-brand dark:hover:text-brand-muted rounded transition-colors"
					title="Copy to clipboard"
				>
					{#if copied}
						<Check size={16} class="text-green-500" />
					{:else}
						<Copy size={16} />
					{/if}
				</button>
			</div>
			<div class="mt-4 flex justify-end">
				<button
					type="button"
					onclick={handleClose}
					class="px-4 py-2 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md transition-colors"
				>
					Done
				</button>
			</div>
		{:else}
			{#if error}
				<FormError variant="box" class="mb-4" message={error} />
			{/if}

			<form onsubmit={handleSubmit} class="space-y-4">
				<div>
					<label for="key-name" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
						Name
					</label>
					<input
						id="key-name"
						type="text"
						bind:value={name}
						placeholder="e.g., Home Assistant, cron script"
						maxlength="100"
						class="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400"
					/>
				</div>

				<fieldset>
					<legend class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Scopes</legend>
					<div class="space-y-2">
						<label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
							<input type="checkbox" bind:checked={scopeRead} class="rounded" data-testid="scope-read" />
							<span><strong>read</strong> — view products, prices, alerts</span>
						</label>
						<label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
							<input type="checkbox" bind:checked={scopeWrite} class="rounded" data-testid="scope-write" />
							<span><strong>write</strong> — create/modify products and alerts</span>
						</label>
					</div>
				</fieldset>

				<div>
					<label for="key-expires" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
						Expires in
					</label>
					<select
						id="key-expires"
						bind:value={expiresIn}
						class="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
					>
						<option value="">Never</option>
						<option value="30">30 days</option>
						<option value="90">90 days</option>
						<option value="180">180 days</option>
						<option value="365">1 year</option>
					</select>
				</div>

				<div class="flex justify-end gap-3 pt-2">
					<button
						type="button"
						onclick={handleClose}
						disabled={saving}
						class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
					>
						Cancel
					</button>
					<button
						type="submit"
						disabled={saving}
						class="px-4 py-2 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50 flex items-center gap-2"
					>
						{#if saving}
							<LoaderCircle size={16} class="animate-spin" />
						{/if}
						Create Key
					</button>
				</div>
			</form>
		{/if}
	</div>
</Modal>
