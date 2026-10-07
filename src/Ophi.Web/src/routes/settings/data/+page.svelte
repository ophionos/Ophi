<script lang="ts">
	import { api } from '$lib/api/client';
	import { LoaderCircle, Download, FileUp } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { downloadBlob } from '$lib/utils/download';
	import BackupCard from '$lib/components/settings/BackupCard.svelte';

	let exportLoading = $state(false);
	let importLoading = $state(false);
	let importFileInput = $state<HTMLInputElement | null>(null);

	async function handleExportProducts(format: 'csv' | 'json') {
		exportLoading = true;
		try {
			const response = await api.exportProducts(format);
			const blob = await response.blob();
			downloadBlob(blob, `ophi-products.${format}`);
			toast.success(`Products exported as ${format.toUpperCase()}`);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to export products');
		} finally {
			exportLoading = false;
		}
	}

	async function handleImportProducts(e: Event) {
		const input = e.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;
		importLoading = true;
		try {
			const result = await api.importProducts(file);
			const parts: string[] = [];
			if (result.added > 0) parts.push(`${result.added} added`);
			if (result.skipped > 0) parts.push(`${result.skipped} skipped`);
			if (result.errors.length > 0) parts.push(`${result.errors.length} errors`);
			toast.success(`Import complete: ${parts.join(', ')}`);
			if (result.errors.length > 0) {
				console.warn('Import errors:', result.errors);
			}
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to import products');
		} finally {
			importLoading = false;
			input.value = '';
		}
	}
</script>

<svelte:head>
	<title>Data Import / Export - Ophi</title>
</svelte:head>

<div class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3">
	<div>
		<p class="text-sm font-medium text-gray-900 dark:text-white">Import / Export Products</p>
		<p class="text-xs text-gray-500 dark:text-gray-400">
			Bulk import from CSV or export your watchlist for backup
		</p>
	</div>

	<div class="flex flex-wrap gap-2">
		<button
			onclick={() => handleExportProducts('csv')}
			disabled={exportLoading}
			class="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-md disabled:opacity-50 transition-colors"
		>
			{#if exportLoading}
				<LoaderCircle size={14} class="animate-spin" />
			{:else}
				<Download size={14} />
			{/if}
			Export CSV
		</button>
		<button
			onclick={() => handleExportProducts('json')}
			disabled={exportLoading}
			class="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-md disabled:opacity-50 transition-colors"
		>
			<Download size={14} />
			Export JSON
		</button>
		<button
			onclick={() => importFileInput?.click()}
			disabled={importLoading}
			class="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-md disabled:opacity-50 transition-colors"
		>
			{#if importLoading}
				<LoaderCircle size={14} class="animate-spin" />
			{:else}
				<FileUp size={14} />
			{/if}
			Import CSV
		</button>
		<input
			type="file"
			accept=".csv"
			class="hidden"
			bind:this={importFileInput}
			onchange={handleImportProducts}
		/>
	</div>

	<p class="text-xs text-gray-400 dark:text-gray-500">
		CSV format: <code class="bg-gray-100 dark:bg-gray-700 px-1 rounded">url,name,target_price,tags</code>. Only <code class="bg-gray-100 dark:bg-gray-700 px-1 rounded">url</code> is required.
	</p>
</div>

<div class="mt-3">
	<BackupCard />
</div>
