<script lang="ts">
	import { Pencil, Trash2, Globe, Settings, FlaskConical, Sparkles, Download, Link } from 'lucide-svelte';
	import type { Store } from '$lib/api/client';

	interface Props {
		store: Store;
		onTest?: (store: Store) => void;
		onEdit?: (store: Store) => void;
		onDelete?: (store: Store) => void;
		onExport?: (store: Store) => void;
	}

	let { store, onTest, onEdit, onDelete, onExport }: Props = $props();

	const hasAffiliate = $derived(!!store.affiliateParamName && !!store.affiliateTag);

	function getSelectorCount() {
		const { priceSelectors, nameSelectors, imageSelectors } = store.selectors;
		return `${priceSelectors.length} price, ${nameSelectors.length} name, ${imageSelectors.length} image`;
	}
</script>

<div
	class="bg-white dark:bg-gray-800 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 p-4 hover:shadow-md transition-shadow"
>
	<div class="flex items-start justify-between">
		<div class="flex-1">
			<div class="flex items-center gap-2">
				<h3 class="font-medium text-gray-900 dark:text-white">{store.name}</h3>
				{#if store.isBuiltIn}
					<span
						class="px-2 py-0.5 text-xs font-medium bg-brand-subtle dark:bg-brand-dark text-brand dark:text-brand-muted rounded-full"
					>
						Built-in
					</span>
				{:else}
					<span
						class="px-2 py-0.5 text-xs font-medium bg-green-100 dark:bg-green-900 text-green-700 dark:text-green-300 rounded-full"
					>
						Custom
					</span>
				{/if}
			{#if store.isAutoCreated && !store.isBuiltIn}
				<span
					class="px-2 py-0.5 text-xs font-medium bg-amber-100 dark:bg-amber-900 text-amber-700 dark:text-amber-300 rounded-full inline-flex items-center gap-1"
				>
					<Sparkles size={12} />
					Auto-detected
				</span>
			{/if}
			{#if hasAffiliate}
				<span
					class="px-2 py-0.5 text-xs font-medium bg-purple-100 dark:bg-purple-900 text-purple-700 dark:text-purple-300 rounded-full inline-flex items-center gap-1"
					data-testid="affiliate-badge"
				>
					<Link size={12} />
					Affiliate
				</span>
			{/if}
			</div>
			<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">{store.storeId}</p>
		</div>

		<div class="flex gap-1">
			{#if onTest}
				<button
					onclick={() => onTest?.(store)}
					class="p-2 text-gray-400 hover:text-purple-600 dark:hover:text-purple-400 hover:bg-purple-50 dark:hover:bg-purple-900/30 rounded"
					title="Test"
				>
					<FlaskConical size={16} />
				</button>
			{/if}
			{#if !store.isBuiltIn && onExport}
				<button
					onclick={() => onExport?.(store)}
					class="p-2 text-gray-400 hover:text-green-600 dark:hover:text-green-400 hover:bg-green-50 dark:hover:bg-green-900/30 rounded"
					title="Export"
				>
					<Download size={16} />
				</button>
			{/if}
		{#if !store.isBuiltIn && onEdit}
				<button
					onclick={() => onEdit?.(store)}
					class="p-2 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
					title="Edit"
				>
					<Pencil size={16} />
				</button>
			{/if}
			{#if !store.isBuiltIn && onDelete}
				<button
					onclick={() => onDelete?.(store)}
					class="p-2 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded"
					title="Delete"
				>
					<Trash2 size={16} />
				</button>
			{/if}
		</div>
	</div>

	<div class="mt-3 space-y-2">
		<div class="flex items-start gap-2">
			<Globe size={14} class="text-gray-400 mt-0.5 shrink-0" />
			<div class="flex flex-wrap gap-1">
				{#each store.domainPatterns as domain (domain)}
					<span
						class="px-2 py-0.5 text-xs bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-300 rounded"
					>
						{domain}
					</span>
				{/each}
			</div>
		</div>

		<div class="flex items-center gap-2">
			<Settings size={14} class="text-gray-400 shrink-0" />
			<span class="text-xs text-gray-500 dark:text-gray-400">{getSelectorCount()} selectors</span>
		</div>
	</div>

	{#if store.createdAt && !store.isBuiltIn}
		<div class="mt-3 pt-3 border-t border-gray-100 dark:border-gray-700">
			<span class="text-xs text-gray-400 dark:text-gray-500">
				Created {new Date(store.createdAt).toLocaleDateString()}
			</span>
		</div>
	{/if}
</div>
