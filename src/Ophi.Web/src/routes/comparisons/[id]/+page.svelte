<script lang="ts">
	import type { PageData } from './$types';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import { api, type ComparisonGroupDetail } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import { comparisons } from '$lib/stores/comparisons.svelte';
	import { liveUpdates } from '$lib/stores/liveUpdates.svelte';
	import ComparisonChart from '$lib/components/comparisons/ComparisonChart.svelte';
	import AddProductToGroupModal from '$lib/components/products/AddProductToGroupModal.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import {
		ArrowLeft,
		Plus,
		Trash2,
		Package,
		ExternalLink,
		Trophy,
		ListTree,
		TrendingUp
	} from 'lucide-svelte';
	import SectionCard from '$lib/components/shared/SectionCard.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	let { data: pageData }: { data: PageData } = $props();

	let groupId = $derived(page.params.id ?? '');
	let group = $derived<ComparisonGroupDetail | null>(pageData.group);
	let selectedDays = $state(7);

	let addModalOpen = $state(false);

	let deleteGroupModalOpen = $state(false);
	let deleteGroupLoading = $state(false);

	let removeProductId = $state<string | null>(null);
	let removeProductLoading = $state(false);

	const existingProductIds = $derived(group?.products.map((p) => p.id) ?? []);

	// Live updates: refresh the group when one of its products is rescraped.
	$effect(() => {
		return liveUpdates.subscribe((evt) => {
			if (
				evt.type === 'scrape-completed' &&
				(!evt.productId || existingProductIds.includes(evt.productId))
			) {
				refreshGroupData();
			}
		});
	});

	// Compute savings vs next cheapest for spotlight
	const savingsVsNext = $derived.by(() => {
		if (!group || group.products.length < 2 || group.bestPrice == null) return null;
		const sorted = group.products
			.filter((p) => p.currentPrice != null)
			.sort((a, b) => a.currentPrice! - b.currentPrice!);
		if (sorted.length < 2) return null;
		return sorted[1].currentPrice! - sorted[0].currentPrice!;
	});

	async function handleDaysChange(days: number) {
		selectedDays = days;
		try {
			group = await api.getComparisonGroup(groupId, days);
		} catch {
			toast.error('Failed to load price history');
		}
	}

	function handleOpenAddProduct() {
		addModalOpen = true;
	}

	function handleCloseAddProduct() {
		addModalOpen = false;
	}

	async function refreshGroupData() {
		group = await api.getComparisonGroup(groupId, selectedDays);
		comparisons.update(groupId, {
			id: groupId,
			name: group.name,
			productCount: group.products.length
		});
	}

	async function handleAddProducts(productIds: string[]) {
		try {
			await api.addProductsToComparisonGroup(groupId, productIds);
			await refreshGroupData();
			handleCloseAddProduct();
			toast.success(
				productIds.length === 1
					? 'Product added to group'
					: `${productIds.length} products added to group`
			);
		} catch (error) {
			console.error('Failed to add products:', error);
			toast.error('Failed to add products to group');
			throw error;
		}
	}

	function handleOpenRemoveProduct(productId: string) {
		removeProductId = productId;
	}

	function handleCloseRemoveProduct() {
		removeProductId = null;
		removeProductLoading = false;
	}

	async function handleConfirmRemoveProduct() {
		if (!removeProductId) return;

		removeProductLoading = true;
		try {
			await api.removeProductFromComparisonGroup(groupId, removeProductId);
			await refreshGroupData();
			handleCloseRemoveProduct();
			toast.success('Product removed from group');
		} catch (error) {
			console.error('Failed to remove product:', error);
			toast.error('Failed to remove product from group');
			removeProductLoading = false;
		}
	}

	function handleOpenDeleteGroup() {
		deleteGroupModalOpen = true;
	}

	function handleCloseDeleteGroup() {
		deleteGroupModalOpen = false;
		deleteGroupLoading = false;
	}

	async function handleConfirmDeleteGroup() {
		deleteGroupLoading = true;
		try {
			await api.deleteComparisonGroup(groupId);
			comparisons.remove(groupId);
			toast.success('Comparison group deleted');
			goto(resolve('/comparisons'));
		} catch (error) {
			console.error('Failed to delete group:', error);
			toast.error('Failed to delete comparison group');
			deleteGroupLoading = false;
		}
	}

	function getRemoveProductName() {
		if (!removeProductId || !group) return '';
		const product = group.products.find((p) => p.id === removeProductId);
		return product?.name ?? '';
	}
</script>

<div>
	<!-- Header -->
	<div class="mb-6">
		<a
			href={resolve('/comparisons')}
			class="inline-flex items-center gap-1 text-sm text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300 mb-4"
		>
			<ArrowLeft size={16} />
			Back to Comparisons
		</a>

		{#if group}
			<div class="flex items-start justify-between">
				<div>
					<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">{group.name}</h1>
					{#if group.description}
						<p class="text-gray-600 dark:text-gray-400 mt-1">{group.description}</p>
					{/if}
				</div>
				<div class="flex gap-2">
					<button
						onclick={handleOpenAddProduct}
						class="px-4 py-2 bg-brand text-white rounded-lg hover:bg-brand-hover flex items-center gap-2"
					>
						<Plus size={18} />
						Add Product
					</button>
					<button
						onclick={handleOpenDeleteGroup}
						class="px-4 py-2 text-red-600 dark:text-red-400 border border-red-200 dark:border-red-800 rounded-lg hover:bg-red-50 dark:hover:bg-red-900/20 flex items-center gap-2"
					>
						<Trash2 size={18} />
						Delete Group
					</button>
				</div>
			</div>
		{/if}
	</div>

	{#if group}
		<!-- Winner Spotlight Banner -->
		{#if group.bestPrice != null && group.bestPriceProductId}
			{@const bestPriceProductId = group.bestPriceProductId}
			{@const bestProduct = group.products.find((p) => p.id === bestPriceProductId)}
			{#if bestProduct}
				<div
					class="mb-6 p-5 bg-gradient-to-r from-green-50 to-emerald-50 dark:from-green-900/20 dark:to-emerald-900/20 border border-green-200 dark:border-green-800 rounded-xl shadow-sm ring-1 ring-green-200/50 dark:ring-green-800/50"
					data-testid="winner-spotlight"
				>
					<div class="flex items-center gap-4">
						<div class="p-3 bg-green-100 dark:bg-green-900/60 rounded-full shadow-inner">
							<Trophy size={24} class="text-green-600 dark:text-green-400" />
						</div>
						<div class="flex-1 min-w-0">
							<div class="flex items-center gap-2">
								<p
									class="text-sm font-semibold text-green-800 dark:text-green-300 uppercase tracking-wide"
								>
									Winner
								</p>
							</div>
							<p class="text-lg font-bold text-green-700 dark:text-green-400 truncate">
								{bestProduct.name}
							</p>
							<p class="text-2xl font-extrabold text-green-800 dark:text-green-300 mt-0.5">
								{formatPrice(group.bestPrice, bestProduct.currency)}
							</p>
						</div>
						{#if savingsVsNext != null && savingsVsNext > 0}
							<div class="shrink-0 text-right">
								<p
									class="text-xs font-medium text-green-600 dark:text-green-400 uppercase tracking-wide"
								>
									You save
								</p>
								<p class="text-xl font-bold text-green-700 dark:text-green-300">
									{formatPrice(savingsVsNext, bestProduct.currency)}
								</p>
								<p class="text-xs text-green-600/70 dark:text-green-400/70">vs. next cheapest</p>
							</div>
						{/if}
					</div>
				</div>
			{/if}
		{/if}

		<!-- Price Chart -->
		<SectionCard title="Price Comparison" icon={TrendingUp}>
			{#snippet action()}
				<div class="flex gap-1">
					{#each [7, 30, 90, 365] as days (days)}
						<button
							onclick={() => handleDaysChange(days)}
							class="px-3 py-1 text-sm rounded-md transition-colors {selectedDays === days
								? 'bg-brand text-white'
								: 'bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}"
						>
							{days === 365 ? '1y' : `${days}d`}
						</button>
					{/each}
				</div>
			{/snippet}
			<ComparisonChart products={group.products} />
		</SectionCard>

		<!-- Product Attributes -->
		{#if group.products.some((p) => p.customFields && p.customFields.length > 0)}
			{@const mergedFields = (() => {
				const fieldNames: string[] = [];
				for (const product of group.products) {
					for (const field of product.customFields ?? []) {
						if (!fieldNames.includes(field.name)) {
							fieldNames.push(field.name);
						}
					}
				}
				return fieldNames;
			})()}
			<SectionCard
				title="Product Attributes"
				icon={ListTree}
				testid="product-attributes-table"
				bodyClass="overflow-x-auto"
			>
				<table class="w-full text-sm">
					<thead>
						<tr class="border-b border-gray-200 dark:border-gray-700">
							<th class="px-4 py-3 text-left font-medium text-gray-500 dark:text-gray-400">
								Attribute
							</th>
							{#each group.products as product (product.id)}
								<th class="px-4 py-3 text-left font-medium text-gray-900 dark:text-white">
									{product.name}
								</th>
							{/each}
						</tr>
					</thead>
					<tbody>
						{#each mergedFields as fieldName (fieldName)}
							<tr class="border-b border-gray-100 dark:border-gray-700 last:border-0">
								<td class="px-4 py-2 font-medium text-gray-600 dark:text-gray-400">
									{fieldName}
								</td>
								{#each group.products as product (product.id)}
									<td class="px-4 py-2 text-gray-900 dark:text-white">
										{product.customFields?.find((f) => f.name === fieldName)?.value ?? '-'}
									</td>
								{/each}
							</tr>
						{/each}
					</tbody>
				</table>
			</SectionCard>
		{/if}

		<!-- Products List -->
		<SectionCard title="Products ({group.products.length})" icon={Package} bodyClass="">
			{#if group.products.length === 0}
				<div>
					<EmptyState
						framed={false}
						icon={Package}
						title="No products in this group"
						description="Add products to compare their prices"
					>
						<button
							onclick={handleOpenAddProduct}
							class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
						>
							<Plus size={16} />
							Add a product to this group
						</button>
					</EmptyState>
				</div>
			{:else}
				<div class="divide-y divide-gray-200 dark:divide-gray-700">
					{#each group.products as product (product.id)}
						<div
							class="p-4 flex items-center gap-4 transition-colors {product.isBestPrice
								? 'bg-green-50/50 dark:bg-green-900/10 ring-1 ring-inset ring-green-200 dark:ring-green-800/50'
								: 'hover:bg-gray-50 dark:hover:bg-gray-700/50'}"
						>
							{#if product.imageUrl}
								<img
									src={product.imageUrl}
									alt=""
									loading="lazy"
									class="{product.isBestPrice
										? 'w-20 h-20'
										: 'w-16 h-16'} object-cover rounded transition-all"
								/>
							{:else}
								<div
									class="{product.isBestPrice
										? 'w-20 h-20'
										: 'w-16 h-16'} bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center transition-all"
								>
									<Package size={product.isBestPrice ? 28 : 24} class="text-gray-400" />
								</div>
							{/if}

							<div class="flex-1 min-w-0">
								<div class="flex items-center gap-2">
									<a
										href={resolve(`/products/${product.id}`)}
										class="{product.isBestPrice
											? 'text-base'
											: 'text-sm'} font-medium text-gray-900 dark:text-white hover:text-brand dark:hover:text-brand-muted truncate"
									>
										{product.name}
									</a>
									{#if product.isBestPrice}
										<span
											class="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-semibold bg-green-100 dark:bg-green-900 text-green-700 dark:text-green-300 rounded-full"
										>
											<Trophy size={10} />
											Best Price
										</span>
									{/if}
								</div>
								{#if product.currentPrice != null}
									<p
										class="{product.isBestPrice
											? 'text-xl font-bold text-green-700 dark:text-green-400'
											: 'text-lg font-semibold text-gray-900 dark:text-white'} mt-1"
									>
										{formatPrice(product.currentPrice, product.currency)}
									</p>
								{:else}
									<p class="text-sm text-gray-400 dark:text-gray-500 mt-1">Price unavailable</p>
								{/if}
							</div>

							<div class="flex items-center gap-2">
								<a
									href={product.affiliateUrl ?? product.url}
									target="_blank"
									rel="noopener noreferrer external"
									class="p-2 text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
									title="Visit store"
								>
									<ExternalLink size={18} />
								</a>
								<button
									onclick={() => handleOpenRemoveProduct(product.id)}
									class="p-2 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded"
									title="Remove from group"
								>
									<Trash2 size={18} />
								</button>
							</div>
						</div>
					{/each}
				</div>
			{/if}
		</SectionCard>
	{/if}
</div>

<AddProductToGroupModal
	isOpen={addModalOpen}
	{existingProductIds}
	onClose={handleCloseAddProduct}
	onAdd={handleAddProducts}
/>

<ConfirmModal
	isOpen={removeProductId !== null}
	title="Remove Product"
	message="Are you sure you want to remove '{getRemoveProductName()}' from this comparison group?"
	confirmText="Remove"
	onConfirm={handleConfirmRemoveProduct}
	onCancel={handleCloseRemoveProduct}
	loading={removeProductLoading}
/>

<ConfirmModal
	isOpen={deleteGroupModalOpen}
	title="Delete Comparison Group"
	message="Are you sure you want to delete this comparison group? This action cannot be undone."
	onConfirm={handleConfirmDeleteGroup}
	onCancel={handleCloseDeleteGroup}
	loading={deleteGroupLoading}
/>
