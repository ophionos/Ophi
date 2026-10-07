<script lang="ts">
	import { FlaskConical, LoaderCircle, CircleCheck, CircleX } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import { api, type Store, type TestStoreResult } from '$lib/api/client';
	import { formatPrice } from '$lib/format';

	interface Props {
		isOpen: boolean;
		store: Store;
		onClose: () => void;
	}

	let { isOpen, store, onClose }: Props = $props();

	let testUrl = $state('');
	let loading = $state(false);
	let result = $state<TestStoreResult | null>(null);
	let error = $state('');

	const title = $derived(`Test ${store.name}`);

	$effect(() => {
		if (!isOpen) {
			testUrl = '';
			loading = false;
			result = null;
			error = '';
		}
	});

	async function handleTest() {
		if (!testUrl.trim()) return;

		loading = true;
		result = null;
		error = '';

		try {
			result = await api.testStore(store.storeId, testUrl);
		} catch (e) {
			error = e instanceof Error ? e.message : 'An error occurred';
		} finally {
			loading = false;
		}
	}

</script>

<Modal {isOpen} {title} size="lg" {onClose}>
	<div class="p-4 space-y-4">
		<div>
			<label
				for="test-url"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
			>
				Test URL
			</label>
			<div class="flex gap-2">
				<input
					id="test-url"
					type="url"
					bind:value={testUrl}
					placeholder="https://example.com/product/123"
					class="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 focus:ring-2 focus:ring-brand focus:border-transparent"
					disabled={loading}
				/>
				<button
					onclick={handleTest}
					disabled={loading || !testUrl.trim()}
					class="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
				>
					{#if loading}
						<LoaderCircle size={16} class="animate-spin" />
					{:else}
						<FlaskConical size={16} />
					{/if}
					Test
				</button>
			</div>
		</div>

		{#if error}
			<div
				class="p-3 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 rounded-lg"
			>
				<div class="flex items-center gap-2">
					<CircleX size={16} class="text-red-600 dark:text-red-400" />
					<p role="alert" class="text-sm text-red-600 dark:text-red-400">{error}</p>
				</div>
			</div>
		{/if}

		{#if result}
			<div
				class="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden"
				data-testid="test-result"
			>
				<div
					class="px-4 py-3 flex items-center gap-2 {result.success ? 'bg-green-50 dark:bg-green-900/20' : 'bg-red-50 dark:bg-red-900/20'}"
				>
					{#if result.success}
						<CircleCheck
							size={18}
							class="text-green-600 dark:text-green-400"
						/>
						<span
							class="text-sm font-medium text-green-700 dark:text-green-300"
							>Extraction successful</span
						>
					{:else}
						<CircleX size={18} class="text-red-600 dark:text-red-400" />
						<span
							class="text-sm font-medium text-red-700 dark:text-red-300"
							>{result.error || 'Extraction failed'}</span
						>
					{/if}
				</div>

				{#if result.success}
					<div class="p-4 space-y-3">
						{#if result.extractedName}
							<div>
								<p
									class="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase"
								>
									Product Name
								</p>
								<p class="text-sm text-gray-900 dark:text-white">
									{result.extractedName}
								</p>
							</div>
						{/if}

						{#if result.extractedPrice != null}
							<div>
								<p
									class="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase"
								>
									Price
								</p>
								<p
									class="text-lg font-semibold text-gray-900 dark:text-white"
								>
									{formatPrice(result.extractedPrice, result.currency ?? 'USD')}
								</p>
							</div>
						{/if}

						{#if result.extractedImageUrl}
							<div>
								<p
									class="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase mb-1"
								>
									Image
								</p>
								<img
									src={result.extractedImageUrl}
									alt="Product"
									class="w-20 h-20 object-contain rounded border border-gray-200 dark:border-gray-700"
								/>
							</div>
						{/if}

						{#if result.detectedSelector}
							<div>
								<p
									class="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase"
								>
									Detected Selector
								</p>
								<code
									class="text-xs bg-gray-100 dark:bg-gray-700 px-2 py-1 rounded text-gray-800 dark:text-gray-200"
								>
									{result.detectedSelector}
								</code>
							</div>
						{/if}
					</div>
				{/if}
			</div>
		{/if}
	</div>
</Modal>
