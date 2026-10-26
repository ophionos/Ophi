<script lang="ts">
	import type { PageData } from './$types';
	import { resolve } from '$app/paths';
	import { api } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import { toast } from '$lib/stores/toast.svelte';
	import { Plus, ShoppingBasket } from 'lucide-svelte';
	import BasketFormModal from '$lib/components/baskets/BasketFormModal.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';
	import PageHeader from '$lib/components/shared/PageHeader.svelte';

	let { data }: { data: PageData } = $props();

	// Rendered straight from the load data so the server HTML already has the cards. A writable
	// $derived: local edits apply at once, and a new load replaces them.
	let baskets = $derived(data.baskets);
	let modalOpen = $state(false);

	async function handleSave(name: string, description?: string) {
		const created = await api.createBasket(name, description);
		baskets = [
			...baskets,
			{
				id: created.id,
				name: created.name,
				description: created.description,
				itemCount: created.items.length,
				updatedAt: created.updatedAt,
				bestStore: created.bestStore,
				bestTotal: null,
				bestCurrency: null
			}
		];
		toast.success(`Basket "${created.name}" created`);
		modalOpen = false;
	}
</script>

<svelte:head>
	<title>Baskets - Ophi</title>
</svelte:head>

<div>
	<PageHeader title="Baskets" description="What your shopping lists cost at each store">
		{#snippet actions()}
			<button
				onclick={() => (modalOpen = true)}
				class="whitespace-nowrap px-4 min-h-11 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
			>
				<Plus size={18} />
				New basket
			</button>
		{/snippet}
	</PageHeader>

	{#if baskets.length === 0}
		<EmptyState
			icon={ShoppingBasket}
			title="No baskets yet"
			description="Products you buy together, with a total per store. Create a basket, then add products from a product page or the watchlist."
		>
			<button
				onclick={() => (modalOpen = true)}
				class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
			>
				<Plus size={16} />
				Create your first basket
			</button>
		</EmptyState>
	{:else}
		<div class="grid md:grid-cols-2 lg:grid-cols-3 gap-4">
			{#each baskets as basket (basket.id)}
				<div
					data-testid="basket-card"
					class="relative min-w-0 bg-surface-1 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700 p-4 hover:shadow-md transition-shadow focus-within:ring-2 focus-within:ring-brand"
				>
					<div class="flex items-center gap-2">
						<ShoppingBasket size={18} class="shrink-0 text-brand dark:text-brand-muted" />
						<h3 class="font-medium text-gray-900 dark:text-white truncate">
							<a
								href={resolve(`/baskets/${basket.id}`)}
								class="after:absolute after:inset-0 after:rounded-lg focus:outline-none hover:text-brand dark:hover:text-brand-muted"
								>{basket.name}</a
							>
						</h3>
					</div>
					<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
						{basket.itemCount}
						{basket.itemCount === 1 ? 'item' : 'items'}
					</p>
					<p class="mt-3 text-sm text-gray-600 dark:text-gray-300">
						{#if basket.bestStore && basket.bestTotal != null && basket.bestCurrency}
							Best: {basket.bestStore} · {formatPrice(basket.bestTotal, basket.bestCurrency)}
						{:else}
							No complete store yet
						{/if}
					</p>
				</div>
			{/each}
		</div>
	{/if}
</div>

<BasketFormModal open={modalOpen} onClose={() => (modalOpen = false)} onSave={handleSave} />
