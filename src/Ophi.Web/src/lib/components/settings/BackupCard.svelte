<script lang="ts">
	import { Download, FileUp, LoaderCircle } from 'lucide-svelte';
	import { api, type BackupImportResult } from '$lib/api/client';
	import { downloadBlob } from '$lib/utils/download';
	import FormError from '$lib/components/shared/FormError.svelte';

	let exporting = $state(false);
	let importing = $state(false);
	let result = $state<BackupImportResult | null>(null);
	let error = $state('');

	async function handleExport() {
		exporting = true;
		error = '';
		try {
			const response = await api.exportBackup();
			const disposition = response.headers.get('Content-Disposition') ?? '';
			const name = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? 'ophi-backup.json';
			downloadBlob(await response.blob(), name);
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to download backup';
		} finally {
			exporting = false;
		}
	}

	async function handleImport(e: Event) {
		const input = e.currentTarget as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;
		importing = true;
		error = '';
		result = null;
		try {
			result = await api.importBackup(file);
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to restore backup';
		} finally {
			importing = false;
			input.value = '';
		}
	}
</script>

<div
	class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3"
	data-testid="backup-card"
>
	<div>
		<p class="text-sm font-medium text-gray-900 dark:text-white">Full account backup</p>
		<p class="text-xs text-gray-500 dark:text-gray-400">
			One file with your settings, tags, comparison groups, store configurations, products, full price
			history and alerts. Passwords, API keys, notification channel links and outbound webhooks are
			never included — re-enter those after a restore.
		</p>
	</div>

	<div class="flex flex-wrap gap-2">
		<button
			onclick={handleExport}
			disabled={exporting}
			class="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50"
		>
			{#if exporting}<LoaderCircle size={16} class="animate-spin" />{:else}<Download size={16} />{/if}
			Download backup
		</button>
		<label
			class="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-md border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:bg-gray-50 dark:hover:bg-gray-700 cursor-pointer {importing
				? 'opacity-50 pointer-events-none'
				: ''}"
		>
			{#if importing}<LoaderCircle size={16} class="animate-spin" />{:else}<FileUp size={16} />{/if}
			Restore from backup
			<input
				type="file"
				accept="application/json,.json"
				class="sr-only"
				aria-label="Restore from a backup file"
				onchange={handleImport}
				disabled={importing}
			/>
		</label>
	</div>

	<p class="text-xs text-gray-400 dark:text-gray-500">
		Restoring only adds: existing products, tags, groups and stores are kept, never overwritten. Settings
		are restored only into an account that has no products yet.
	</p>

	{#if error}
		<FormError variant="box" message={error} />
	{/if}

	{#if result}
		<div
			role="status"
			class="p-3 rounded-md bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 text-sm text-green-800 dark:text-green-300 space-y-1"
		>
			<p>
				{result.productsAdded} products added{#if result.productsSkipped > 0}, {result.productsSkipped}
					already tracked (skipped){/if}. {result.pricePointsAdded} price points, {result.alertsAdded}
				alerts, {result.tagsAdded} tags, {result.comparisonGroupsAdded} comparison groups and {result.storesAdded}
				stores added{#if result.storesKept > 0} ({result.storesKept} existing kept){/if}.
				{result.settingsRestored ? 'Settings restored.' : ''}
			</p>
			{#each result.warnings as warning, i (i)}
				<p class="text-amber-700 dark:text-amber-400">{warning}</p>
			{/each}
		</div>
	{/if}
</div>
