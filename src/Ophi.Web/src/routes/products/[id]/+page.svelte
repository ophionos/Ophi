<script lang="ts">
	import type { PageData } from './$types';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import {
		api,
		type ProductDetail,
		type PriceHistory,
		type ComparisonGroup,
		type AlertCondition
	} from '$lib/api/client';
	import { tags } from '$lib/stores/tags.svelte';
	import PriceChart from '$lib/components/products/PriceChart.svelte';
	import AlertModal from '$lib/components/alerts/AlertModal.svelte';
	import AlertList from '$lib/components/alerts/AlertList.svelte';
	import ProductEditModal from '$lib/components/products/ProductEditModal.svelte';
	import ProductHero from '$lib/components/products/ProductHero.svelte';
	import PriceStats from '$lib/components/products/PriceStats.svelte';
	import ComparisonGroupPanel from '$lib/components/products/ComparisonGroupPanel.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import ProductUrlCard from '$lib/components/products/ProductUrlCard.svelte';
	import AddUrlModal from '$lib/components/products/AddUrlModal.svelte';
	import ChallengeModal from '$lib/components/products/ChallengeModal.svelte';
	import ScrapeHistory from '$lib/components/products/ScrapeHistory.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import {
		ArrowLeft,
		ExternalLink,
		Bell,
		BellPlus,
		Pencil,
		Link,
		Plus,
		ChevronRight,
		TrendingUp as TrendIcon,
		PackageX,
		TriangleAlert,
		ListTree,
		Scale
	} from 'lucide-svelte';
	import { fly } from 'svelte/transition';
	import { isBuiltInStoreUrl } from '$lib/utils/storeMatch';
	import { isAwaitingScrape } from '$lib/utils/awaitingScrape';
	import { liveRefresh } from '$lib/utils/liveRefresh';
	import SectionCard from '$lib/components/shared/SectionCard.svelte';

	let { data: pageData }: { data: PageData } = $props();

	let productId = $derived(page.params.id ?? '');
	let product = $state<ProductDetail | null>(null);
	let priceHistory = $state<PriceHistory | null>(null);
	let comparisonGroups = $state<ComparisonGroup[]>([]);

	$effect(() => {
		product = pageData.product;
		priceHistory = pageData.priceHistory;
		comparisonGroups = pageData.comparisonGroups;
		tags.set(pageData.tags);
	});

	let historyDays = $state(7);
	let historyLoading = $state(false);
	let showAlertModal = $state(false);
	let showEditModal = $state(false);
	let showDeleteConfirm = $state(false);
	let showAddUrlModal = $state(false);
	let challengeUrlId = $state<string | null>(null);

	// Live-refresh while a scrape is in flight (e.g. a just-added URL). The page hydrates from an
	// SSR snapshot and otherwise only re-fetches on user actions, so a scrape that finishes after
	// load would show "No price yet" until manual reload. Mirrors the dashboard's pending-poll.
	const POLL_INTERVAL_MS = 3000;
	const MAX_POLLS = 20; // hard cap (~60s) so a persistently failing URL can't poll forever
	const awaitingScrape = $derived(isAwaitingScrape(product));

	async function refreshProduct() {
		try {
			const [refreshed, history] = await Promise.all([
				api.getProduct(productId),
				api.getPriceHistory(productId, historyDays)
			]);
			product = refreshed;
			priceHistory = history;
		} catch {
			// transient — leave current state in place; the next push/poll will retry
		}
	}

	// Live updates: refetch when this product's scrape completes (ignore pings for other
	// products), with a capped fallback poll while SSE is down and a scrape is still awaited.
	$effect(() =>
		liveRefresh({
			matches: (evt) => evt.type === 'scrape-completed' && evt.productId === productId,
			refresh: refreshProduct,
			shouldPoll: () => awaitingScrape,
			intervalMs: POLL_INTERVAL_MS,
			maxPolls: MAX_POLLS
		})
	);

	const bestPriceUrlId = $derived.by(() => {
		if (!product?.urls) return null;
		const withPrice = product.urls.filter((u) => u.currentPrice != null);
		if (withPrice.length <= 1) return null;
		return withPrice.reduce((best, u) => (u.currentPrice! < best.currentPrice! ? u : best)).id;
	});

	const historyPointCount = $derived(priceHistory?.history?.length ?? 0);

	async function handleHistoryDaysChange(days: number) {
		historyDays = days;
		historyLoading = true;
		try {
			priceHistory = await api.getPriceHistory(productId, days);
		} catch {
			toast.error('Failed to load price history');
		} finally {
			historyLoading = false;
		}
	}

	async function handleCreateAlert(targetPrice: number, condition: AlertCondition) {
		try {
			const created = await api.createAlert(productId, targetPrice, condition);
			showAlertModal = false;
			// The response carries the created alert — append it instead of refetching the product.
			if (product) {
				product.alerts = [
					...product.alerts,
					{
						id: created.id,
						targetPrice: created.targetPrice,
						condition: created.condition,
						active: created.active,
						lastTriggered: created.lastTriggered,
						currency: created.currency,
						hasCurrencyMismatch: created.hasCurrencyMismatch
					}
				];
			}
			toast.success('Alert created');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to create alert');
		}
	}

	async function handleRedenominateAlert(alertId: string, targetPrice: number) {
		if (!product) return;
		// The server re-reads the product's currency and refuses if it no longer matches what we
		// were showing, so send the currency the user actually saw rather than assuming it held.
		const updated = await api.redenominateAlert(alertId, targetPrice, product.currency);
		product.alerts = product.alerts.map((a) =>
			a.id === alertId
				? {
						...a,
						targetPrice: updated.targetPrice,
						currency: updated.currency,
						hasCurrencyMismatch: updated.hasCurrencyMismatch
					}
				: a
		);
		toast.success(`Alert target set in ${updated.currency}`);
	}

	async function handleToggleAlert(alertId: string) {
		const alert = product?.alerts.find((a) => a.id === alertId);
		if (!product || !alert) return;
		try {
			const updated = await api.setAlertActive(alertId, !alert.active);
			product.alerts = product.alerts.map((a) =>
				a.id === alertId ? { ...a, active: updated.active } : a
			);
			toast.success(updated.active ? 'Alert resumed' : 'Alert paused');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update alert');
		}
	}

	async function handleDeleteAlert(alertId: string) {
		try {
			await api.deleteAlert(alertId);
			if (product) {
				product.alerts = product.alerts.filter((a) => a.id !== alertId);
			}
			toast.success('Alert deleted');
		} catch {
			toast.error('Failed to delete alert');
		}
	}

	async function handleEditProduct(data: {
		name?: string;
		imageUrl?: string;
		status?: import('$lib/api/client').ProductStatus;
		customFields?: import('$lib/api/client').CustomField[];
		checkIntervalMinutes?: number | null;
	}) {
		try {
			await api.updateProduct(productId, data);
			showEditModal = false;
			product = await api.getProduct(productId);
			toast.success('Product updated');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update product');
		}
	}

	async function handleDeleteProduct() {
		try {
			await api.deleteProduct(productId);
			toast.success('Product deleted');
			goto(resolve('/dashboard'));
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to delete product');
		}
	}

	async function handleAddUrl(url: string) {
		try {
			await api.addProductUrl(productId, url);
			showAddUrlModal = false;
			product = await api.getProduct(productId);
			toast.success('URL added');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to add URL');
		}
	}

	async function handleRemoveUrl(urlId: string) {
		if (!product) return;
		try {
			await api.removeProductUrl(productId, urlId);
			product = await api.getProduct(productId);
			toast.success('URL removed');
		} catch {
			toast.error('Failed to remove URL');
		}
	}

	async function handleRetryScrape(urlId: string) {
		try {
			await api.retryScrapeProductUrl(productId, urlId);
			toast.success('Scrape retry triggered');
		} catch {
			toast.error('Failed to retry scrape');
		}
	}

	async function handleChallengeSolved() {
		challengeUrlId = null;
		try {
			product = await api.getProduct(productId);
		} catch {
			// The live refresh picks the new state up.
		}
	}

	async function handleResumeUrl(urlId: string) {
		try {
			await api.resumeProductUrl(productId, urlId);
			product = await api.getProduct(productId);
			toast.success('URL monitoring resumed');
		} catch {
			toast.error('Failed to resume URL');
		}
	}

	async function handleAddToGroup(groupId: string) {
		if (!groupId) return;
		try {
			await api.addProductToComparisonGroup(groupId, productId);
			const [productData, groupsData] = await Promise.all([
				api.getProduct(productId),
				api.getComparisonGroups()
			]);
			product = productData;
			comparisonGroups = groupsData.items;
			toast.success('Added to comparison group');
		} catch {
			toast.error('Failed to add to group');
		}
	}

	async function handleRemoveFromGroup() {
		if (!product?.comparisonGroupId) return;
		try {
			await api.removeProductFromComparisonGroup(product.comparisonGroupId, productId);
			const [productData, groupsData] = await Promise.all([
				api.getProduct(productId),
				api.getComparisonGroups()
			]);
			product = productData;
			comparisonGroups = groupsData.items;
			toast.success('Removed from comparison group');
		} catch {
			toast.error('Failed to remove from group');
		}
	}

	async function handleAddTag(tagId: string) {
		try {
			await api.addTagToProduct(productId, tagId);
			// The full tag object is already in the loaded tag list — attach it locally.
			const tag = tags.items.find((t) => t.id === tagId);
			if (product && tag) {
				product.tags = [...product.tags, { id: tag.id, name: tag.name, color: tag.color }];
			} else {
				product = await api.getProduct(productId);
			}
		} catch {
			toast.error('Failed to add tag');
		}
	}

	async function handleRemoveTag(tagId: string) {
		try {
			await api.removeTagFromProduct(productId, tagId);
			if (product) {
				product.tags = product.tags.filter((t) => t.id !== tagId);
			}
		} catch {
			toast.error('Failed to remove tag');
		}
	}

	function getStatusBadgeClass(status: string) {
		switch (status) {
			case 'active':
				return 'bg-green-100 dark:bg-green-900 text-green-800 dark:text-green-200';
			case 'error':
				return 'bg-red-100 dark:bg-red-900 text-red-800 dark:text-red-200';
			case 'paused':
				return 'bg-yellow-100 dark:bg-yellow-900 text-yellow-800 dark:text-yellow-200';
			default:
				return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200';
		}
	}

</script>

<svelte:head>
	<title>{product?.name ?? 'Product'} - Ophi</title>
</svelte:head>

{#if product}
	<div class="max-w-6xl mx-auto">
		<!-- Breadcrumb -->
		<nav
			class="flex items-center gap-1 text-sm text-gray-500 dark:text-gray-400 mb-4"
			data-testid="breadcrumbs"
		>
			<a
				href={resolve('/dashboard')}
				class="hover:text-brand dark:hover:text-brand-muted transition-colors">Dashboard</a
			>
			<ChevronRight size={14} />
			<span class="text-gray-900 dark:text-white truncate max-w-xs">{product.name}</span>
		</nav>

		<!-- Header -->
		<div class="flex items-center gap-4 mb-6">
			<button
				onclick={() => goto(resolve('/dashboard'))}
				class="p-2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg shrink-0"
			>
				<ArrowLeft size={20} />
			</button>
			<div class="flex-1 flex items-center gap-3 min-w-0">
				{#if product.urls && product.urls.length > 1}
					<span
						class="text-sm font-medium text-gray-600 dark:text-gray-300 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 px-3 py-1 rounded-full shadow-sm"
					>
						Tracking {product.urls.length} stores
					</span>
				{:else}
					<a
						href={product.affiliateUrl ?? product.url}
						target="_blank"
						rel="noopener noreferrer external"
						class="text-sm font-medium text-brand dark:text-brand-muted hover:text-brand-hover dark:hover:text-brand-muted bg-brand-light dark:bg-brand-dark/20 px-3 py-1 rounded-full flex items-center gap-1.5 transition-colors"
					>
						View on store
						<ExternalLink size={14} />
					</a>
				{/if}
			</div>
			{#if product.status !== 'pending'}
				<button
					onclick={() => (showEditModal = true)}
					class="p-2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
					title="Edit product"
				>
					<Pencil size={18} />
				</button>
			{/if}
			<span
				class="px-2.5 py-1 text-xs font-medium rounded-full capitalize {getStatusBadgeClass(
					product.status
				)}"
			>
				{product.status}
			</span>
		</div>

		<!-- Out of Stock Banner -->
		{#if product.isOutOfStock}
			<div
				class="flex items-center gap-3 p-4 mb-6 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800/50 rounded-xl"
				data-testid="out-of-stock-banner"
				in:fly={{ y: 20, duration: 500, delay: 50 }}
			>
				<PackageX size={20} class="text-red-600 dark:text-red-400 shrink-0" />
				<div>
					<p class="text-sm font-medium text-red-800 dark:text-red-200">
						This product is out of stock
					</p>
					<p class="text-xs text-red-600 dark:text-red-400 mt-0.5">
						All tracked URLs report this product as unavailable. The last known price is preserved.
					</p>
				</div>
			</div>
		{/if}

		<!-- Price Anomaly Banner -->
		{#if product.hasPriceAnomaly}
			<div
				class="flex items-center gap-3 p-4 mb-6 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/50 rounded-xl"
				data-testid="anomaly-banner"
				in:fly={{ y: 20, duration: 500, delay: 50 }}
			>
				<TriangleAlert size={20} class="text-amber-600 dark:text-amber-400 shrink-0" />
				<div>
					<p class="text-sm font-medium text-amber-800 dark:text-amber-200">
						Price anomaly detected
					</p>
					<p class="text-xs text-amber-600 dark:text-amber-400 mt-0.5">
						The latest price change exceeded the anomaly threshold. This may indicate a data issue
						or an unusual price swing.
					</p>
				</div>
			</div>
		{/if}

		<ProductHero {product} />

		<!-- Statistics -->
		<PriceStats {product} {bestPriceUrlId} />

		<!-- Detail sections: two columns on desktop (main = price-focused, rail = metadata) -->
		<div class="grid grid-cols-1 lg:grid-cols-3 gap-6 items-start">
			<!-- Main column -->
			<div class="lg:col-span-2">
				<!-- Price History Chart -->
				<div in:fly={{ y: 20, duration: 500, delay: 400 }}>
					<SectionCard title="Price History" icon={TrendIcon}>
						{#snippet action()}
							<div class="flex gap-2">
								{#each [7, 30, 90, 365] as days (days)}
									<button
										onclick={() => handleHistoryDaysChange(days)}
										class="px-3 py-1 text-sm rounded-md {historyDays === days
											? 'bg-brand text-white'
											: 'bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}"
									>
										{days === 365 ? '1y' : `${days}d`}
									</button>
								{/each}
							</div>
						{/snippet}
						{#if historyPointCount < 2}
							<div
								class="h-64 flex flex-col items-center justify-center text-gray-400 dark:text-gray-500"
								data-testid="chart-empty-state"
							>
								<TrendIcon size={48} class="mb-3 opacity-40" />
								<p class="text-sm font-medium">Start tracking to see price trends</p>
								<p class="text-xs mt-1">Price data will appear here after the first check</p>
							</div>
						{:else}
							<PriceChart
								history={priceHistory?.history ?? []}
								currency={product.currency}
								urlHistories={priceHistory?.urlHistories}
								loading={historyLoading}
							/>
						{/if}
					</SectionCard>
				</div>

				<!-- Store URLs Section -->
				{#if product.urls}
					<div in:fly={{ y: 20, duration: 500, delay: 500 }}>
						<SectionCard title="Store URLs" icon={Link} bodyClass="p-4 space-y-2">
							{#snippet action()}
								<button
									onclick={() => (showAddUrlModal = true)}
									class="px-3 py-1.5 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover flex items-center gap-1"
								>
									<Plus size={16} />
									Add URL
								</button>
							{/snippet}
							{#each product.urls as productUrl (productUrl.id)}
								<ProductUrlCard
									{productUrl}
									canDelete={product.urls.length > 1}
									isBestPrice={product.urls.length > 1 && productUrl.id === bestPriceUrlId}
									isBuiltInStore={isBuiltInStoreUrl(pageData.stores, productUrl.url)}
									onDelete={handleRemoveUrl}
									onRetry={handleRetryScrape}
									onResume={handleResumeUrl}
									onSolveChallenge={pageData.challengeAvailable
										? (urlId) => (challengeUrlId = urlId)
										: undefined}
								/>
							{/each}
						</SectionCard>
					</div>
				{/if}

				<!-- Alerts Section -->
				<div in:fly={{ y: 20, duration: 500, delay: 600 }}>
					<SectionCard title="Price Alerts" icon={Bell}>
						{#snippet action()}
							<button
								onclick={() => (showAlertModal = true)}
								class="px-3 py-1.5 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover flex items-center gap-1"
							>
								<BellPlus size={16} />
								Create Alert
							</button>
						{/snippet}
						<AlertList
							alerts={product.alerts}
							productCurrency={product.currency}
							productCurrentPrice={product.currentPrice}
							onDelete={handleDeleteAlert}
							onRedenominate={handleRedenominateAlert}
							onToggleActive={handleToggleAlert}
						/>
					</SectionCard>
				</div>

				<!-- Scrape History — diagnostic and collapsed, so it follows the alerts. -->
				<ScrapeHistory {productId} currency={product.currency} />
			</div>
			<!-- Right rail — sticky so the secondary panels track the viewport while the taller
			     main column scrolls, instead of leaving a large void at the bottom. -->
			<div class="lg:col-span-1 lg:sticky lg:top-20">
				<!-- Custom Fields -->
				{#if product.customFields && product.customFields.length > 0}
					<div in:fly={{ y: 20, duration: 500, delay: 350 }}>
						<SectionCard title="Custom Fields" icon={ListTree} testid="custom-fields-section">
							<table class="w-full text-sm">
								<tbody>
									{#each product.customFields as field (field.name)}
										<tr class="border-b border-gray-100 dark:border-gray-700 last:border-0">
											<td class="py-2 pr-4 font-medium text-gray-600 dark:text-gray-400 w-1/3">
												{field.name}
											</td>
											<td class="py-2 text-gray-900 dark:text-white">{field.value}</td>
										</tr>
									{/each}
								</tbody>
							</table>
						</SectionCard>
					</div>
				{/if}

				<!-- Comparison Group Section -->
				<div in:fly={{ y: 20, duration: 500, delay: 700 }}>
					<SectionCard title="Comparison Group" icon={Scale}>
						<ComparisonGroupPanel
							groups={comparisonGroups}
							currentGroupId={product.comparisonGroupId}
							onAdd={handleAddToGroup}
							onRemove={handleRemoveFromGroup}
						/>
					</SectionCard>
				</div>

				<!-- Danger Zone — Collapsible -->
				<div in:fly={{ y: 20, duration: 500, delay: 800 }}>
					<SectionCard
						title="Danger Zone"
						icon={TriangleAlert}
						collapsible
						open={false}
						accent="danger"
						testid="danger-zone"
					>
						<div class="flex items-center justify-between gap-4">
							<div>
								<p class="font-medium text-gray-900 dark:text-white">Delete this product</p>
								<p class="text-sm text-gray-500 dark:text-gray-400">
									This will permanently delete the product and all its price history.
								</p>
							</div>
							<button
								onclick={() => (showDeleteConfirm = true)}
								class="shrink-0 whitespace-nowrap px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700"
							>
								Delete Product
							</button>
						</div>
					</SectionCard>
				</div>
			</div>
		</div>
	</div>

	<!-- Edit Modal -->
	<ProductEditModal
		isOpen={showEditModal}
		{product}
		availableTags={tags.items}
		onClose={() => (showEditModal = false)}
		onSave={handleEditProduct}
		onAddTag={handleAddTag}
		onRemoveTag={handleRemoveTag}
	/>

	<!-- Alert Modal -->
	<AlertModal
		isOpen={showAlertModal}
		productName={product.name}
		currentPrice={product.currentPrice}
		currency={product.currency}
		existingAlerts={product.alerts}
		onClose={() => (showAlertModal = false)}
		onSave={handleCreateAlert}
	/>

	<!-- Add URL Modal -->
	<AddUrlModal
		isOpen={showAddUrlModal}
		onClose={() => (showAddUrlModal = false)}
		onSave={handleAddUrl}
	/>

	{#if challengeUrlId}
		<ChallengeModal
			isOpen
			{productId}
			urlId={challengeUrlId}
			onClose={() => (challengeUrlId = null)}
			onSolved={handleChallengeSolved}
		/>
	{/if}

	<!-- Delete Confirmation Modal -->
	<ConfirmModal
		isOpen={showDeleteConfirm}
		title="Delete Product"
		message="Are you sure you want to delete this product? This action cannot be undone and all price history will be lost."
		confirmText="Delete"
		onConfirm={handleDeleteProduct}
		onCancel={() => (showDeleteConfirm = false)}
	/>
{/if}
