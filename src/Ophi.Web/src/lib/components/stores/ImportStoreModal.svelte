<script lang="ts">
	import { Upload, FileBraces } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import type { StoreImportRequest } from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		onClose: () => void;
		onImport: (data: StoreImportRequest) => Promise<void>;
	}

	let { isOpen, onClose, onImport }: Props = $props();

	let jsonText = $state('');
	let error = $state('');
	let loading = $state(false);
	let parsed = $state<StoreImportRequest | null>(null);

	$effect(() => {
		if (!isOpen) {
			jsonText = '';
			error = '';
			loading = false;
			parsed = null;
		}
	});

	function handleTextChange(value: string) {
		jsonText = value;
		error = '';
		parsed = null;

		if (!value.trim()) return;

		try {
			const data = JSON.parse(value);
			if (!data.storeId || !data.name || !data.domainPatterns || !data.selectors) {
				error = 'Missing required fields: storeId, name, domainPatterns, selectors';
				return;
			}
			parsed = {
				storeId: data.storeId,
				name: data.name,
				domainPatterns: data.domainPatterns,
				selectors: data.selectors,
				priceLocale: data.priceLocale,
				requiresJavaScript: data.requiresJavaScript
			};
		} catch {
			error = 'Invalid JSON format';
		}
	}

	async function handleFileUpload(event: Event) {
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;

		const text = await file.text();
		handleTextChange(text);
	}

	async function handleImport() {
		if (!parsed) return;
		loading = true;
		error = '';
		try {
			await onImport(parsed);
		} catch (err) {
			error = err instanceof Error ? err.message : 'Import failed';
			loading = false;
		}
	}
</script>

<Modal {isOpen} title="Import Store Configuration" size="lg" position="center" {onClose}>
	<div class="p-4 space-y-4">
		<div>
			<label
				for="import-store-json"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
			>
				Upload JSON file
			</label>
			<label
				class="flex items-center justify-center gap-2 p-4 border-2 border-dashed border-gray-300 dark:border-gray-600 rounded-lg cursor-pointer hover:border-brand-muted dark:hover:border-brand transition-colors"
			>
				<FileBraces size={20} class="text-gray-400" />
				<span class="text-sm text-gray-500 dark:text-gray-400">Choose a .json file</span>
				<input
					id="import-store-json"
					type="file"
					accept=".json"
					class="hidden"
					onchange={handleFileUpload}
				/>
			</label>
		</div>

		<div class="relative flex items-center">
			<div class="grow border-t border-gray-200 dark:border-gray-700"></div>
			<span class="shrink mx-3 text-xs text-gray-400 uppercase">or paste JSON</span>
			<div class="grow border-t border-gray-200 dark:border-gray-700"></div>
		</div>

		<div>
			<textarea
				value={jsonText}
				oninput={(e) => handleTextChange(e.currentTarget.value)}
				placeholder={'{"storeId": "my-store", "name": "My Store", ...}'}
				rows={8}
				class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md text-sm font-mono bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 focus:ring-2 focus:ring-brand focus:border-transparent"
			></textarea>
		</div>

		{#if error}
			<FormError message={error} />
		{/if}

		{#if parsed}
			<div class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-3 space-y-1">
				<p class="text-sm font-medium text-gray-900 dark:text-white">Preview</p>
				<p class="text-xs text-gray-600 dark:text-gray-400">
					Store ID: <span class="font-mono">{parsed.storeId}</span>
				</p>
				<p class="text-xs text-gray-600 dark:text-gray-400">
					Name: {parsed.name}
				</p>
				<p class="text-xs text-gray-600 dark:text-gray-400">
					Domains: {parsed.domainPatterns.join(', ')}
				</p>
			</div>
		{/if}
	</div>

	<div class="flex justify-end gap-3 p-4 border-t border-gray-200 dark:border-gray-700">
		<button
			onclick={onClose}
			class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600"
		>
			Cancel
		</button>
		<button
			onclick={handleImport}
			disabled={!parsed || loading}
			class="px-4 py-2 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
		>
			<Upload size={16} />
			{loading ? 'Importing...' : 'Import'}
		</button>
	</div>
</Modal>
