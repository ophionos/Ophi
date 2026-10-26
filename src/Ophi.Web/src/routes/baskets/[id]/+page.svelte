<script lang="ts">
	import type { PageData } from './$types';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { api, ApiError, type Basket, type BasketHistory, type BasketItem } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';
	import { ArrowLeft, Pencil, Trash2, Package, ListChecks, Scale, Split, ChartLine } from 'lucide-svelte';
	import BasketFormModal from '$lib/components/baskets/BasketFormModal.svelte';
	import BasketStoreTable from '$lib/components/baskets/BasketStoreTable.svelte';
	import BasketSplitCard from '$lib/components/baskets/BasketSplitCard.svelte';
	import BasketHistoryChart from '$lib/components/baskets/BasketHistoryChart.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';
	import PageHeader from '$lib/components/shared/PageHeader.svelte';
	import SectionCard from '$lib/components/shared/SectionCard.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	let { data }: { data: PageData } = $props();

	// A writable $derived: edits replace it with the server's answer, a new load resets it.
	let basket = $derived<Basket | null>(data.basket);
	const shippingRates = $derived(data.shippingRates ?? []);
	let history = $derived<BasketHistory | null>(data.history ?? null);

	let renameOpen = $state(false);
	let deleteOpen = $state(false);
	let deleteLoading = $state(false);
	let quantityError = $state<{ productId: string; message: string } | null>(null);

	// A quantity or item change moves every past total, so the chart is fetched again.
	async function refreshHistory(id: string) {
		history = await api.getBasketHistory(id, history?.days ?? 30).catch(() => null);
	}

	async function handleRename(name: string, description?: string) {
		if (!basket) return;
		basket = await api.updateBasket(basket.id, name, description);
		renameOpen = false;
		toast.success('Basket renamed');
	}

	async function handleDelete() {
		if (!basket) return;
		deleteLoading = true;
		try {
			await api.deleteBasket(basket.id);
			toast.success('Basket deleted');
			goto(resolve('/baskets'));
		} catch (err) {
			console.error('Failed to delete basket:', err);
			toast.error('Failed to delete basket');
			deleteLoading = false;
		}
	}

	async function handleQuantity(item: BasketItem, input: HTMLInputElement) {
		if (!basket) return;
		const quantity = Number(input.value);
		quantityError = null;
		if (quantity === item.quantity) return;
		try {
			basket = await api.setBasketItemQuantity(basket.id, item.productId, quantity);
			void refreshHistory(basket.id);
		} catch (err) {
			quantityError = {
				productId: item.productId,
				// A 400 carries a generic title; the reason is under the field.
				message:
					(err instanceof ApiError ? err.details.quantity?.[0] : undefined) ??
					(err instanceof Error ? err.message : 'Failed to update quantity')
			};
			input.value = String(item.quantity);
		}
	}

	async function handleRemove(item: BasketItem) {
		if (!basket) return;
		try {
			await api.removeBasketItem(basket.id, item.productId);
			basket = await api.getBasket(basket.id);
			void refreshHistory(basket.id);
		} catch (err) {
			console.error('Failed to remove item:', err);
			toast.error('Failed to remove item from basket');
		}
	}
</script>

<svelte:head>
	<title>{basket?.name ?? 'Basket'} - Ophi</title>
</svelte:head>

<div>
	<div class="mb-6">
		<a
			href={resolve('/baskets')}
			class="inline-flex min-h-11 items-center gap-1 text-sm text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300 mb-4"
		>
			<ArrowLeft size={16} />
			Back to Baskets
		</a>

		{#if basket}
			<PageHeader title={basket.name} description={basket.description ?? undefined}>
				{#snippet actions()}
					<button
						onclick={() => (renameOpen = true)}
						class="whitespace-nowrap px-4 min-h-11 text-gray-700 dark:text-gray-300 bg-surface-field border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-surface-field-hover flex items-center gap-2"
					>
						<Pencil size={18} />
						Rename
					</button>
					<button
						onclick={() => (deleteOpen = true)}
						class="whitespace-nowrap px-4 min-h-11 text-danger border border-danger/30 rounded-lg hover:bg-danger/10 flex items-center gap-2"
					>
						<Trash2 size={18} />
						Delete basket
					</button>
				{/snippet}
			</PageHeader>
		{/if}
	</div>

	{#if basket}
		<SectionCard title="Items ({basket.items.length})" icon={ListChecks} bodyClass="">
			{#if basket.items.length === 0}
				<EmptyState
					framed={false}
					icon={Package}
					title="No items in this basket"
					description="Add products from a product page or from the watchlist selection."
				/>
			{:else}
				<ul class="divide-y divide-gray-200 dark:divide-gray-700" data-testid="basket-items">
					{#each basket.items as item (item.productId)}
						<li class="p-4">
							<div class="flex items-center gap-3">
								<a
									href={resolve(`/products/${item.productId}`)}
									class="min-w-0 flex-1 truncate text-sm font-medium text-gray-900 dark:text-white hover:text-brand dark:hover:text-brand-muted"
									>{item.productName}</a
								>
								<input
									type="number"
									min="1"
									max="999"
									step="1"
									value={item.quantity}
									onchange={(e) => handleQuantity(item, e.currentTarget)}
									aria-label="Quantity of {item.productName}"
									class="w-20 px-3 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md bg-surface-field text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent"
								/>
								<button
									onclick={() => handleRemove(item)}
									class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-danger hover:bg-danger/10 rounded"
									aria-label="Remove {item.productName}"
									title="Remove from basket"
								>
									<Trash2 size={18} />
								</button>
							</div>
							{#if quantityError?.productId === item.productId}
								<FormError message={quantityError.message} />
							{/if}
						</li>
					{/each}
				</ul>
			{/if}
		</SectionCard>

		{#if basket.items.length > 0}
			<div class="mt-6">
				<SectionCard title="Total per store" icon={Scale} bodyClass="">
					{#if basket.stores.length === 0}
						<p class="p-4 text-sm text-gray-500 dark:text-gray-400">
							No store has a price for these products yet.
						</p>
					{:else}
						<BasketStoreTable {basket} {shippingRates} />
					{/if}
				</SectionCard>
			</div>

			{#if basket.split && basket.split.lines.length > 0}
				<div class="mt-6">
					<SectionCard title="Cheapest split" icon={Split} bodyClass="">
						<BasketSplitCard {basket} {shippingRates} />
					</SectionCard>
				</div>
			{/if}

			<div class="mt-6">
				<SectionCard title="Total over time" icon={ChartLine}>
					{#if history}
						<BasketHistoryChart series={history.series} />
					{:else}
						<p class="text-sm text-gray-500 dark:text-gray-400">
							The history is not available right now.
						</p>
					{/if}
				</SectionCard>
			</div>
		{/if}
	{/if}
</div>

<BasketFormModal
	open={renameOpen}
	basket={basket ?? undefined}
	onClose={() => (renameOpen = false)}
	onSave={handleRename}
/>

<ConfirmModal
	isOpen={deleteOpen}
	title="Delete Basket"
	message="Are you sure you want to delete this basket? This action cannot be undone."
	onConfirm={handleDelete}
	onCancel={() => (deleteOpen = false)}
	loading={deleteLoading}
/>
