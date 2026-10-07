<script lang="ts">
	import { resolve } from '$app/paths';
	import { Bell, LoaderCircle, Pause, Play, Trash2, TriangleAlert } from 'lucide-svelte';
	import type { ProductAlert } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import RedenominateAlertForm from '$lib/components/alerts/RedenominateAlertForm.svelte';

	interface Props {
		alert: ProductAlert;
		/** The product's current currency — what a dormant alert must be re-denominated into. */
		productCurrency: string;
		productCurrentPrice?: number;
		/** When set, the row names and links its product (used where alerts span products). */
		product?: { id: string; name: string };
		onDelete: () => Promise<void>;
		/** Rejections are shown in the redenominate form, which stays open. */
		onRedenominate: (targetPrice: number) => Promise<void>;
		/** When set, the row offers Pause / Resume. */
		onToggleActive?: () => Promise<void>;
		/** When set, the row renders a selection checkbox (bulk actions). */
		onSelectChange?: (selected: boolean) => void;
		selected?: boolean;
	}

	let {
		alert,
		productCurrency,
		productCurrentPrice,
		product,
		onDelete,
		onRedenominate,
		onToggleActive,
		onSelectChange,
		selected = false
	}: Props = $props();

	let deleting = $state(false);
	let toggling = $state(false);
	let redenominating = $state(false);

	async function handleToggle() {
		if (!onToggleActive) return;
		toggling = true;
		try {
			await onToggleActive();
		} finally {
			toggling = false;
		}
	}

	const conditionLabels: Record<string, string> = {
		below: 'Below',
		above: 'Above',
		percentDrop: 'Drop %'
	};

	async function handleDelete() {
		deleting = true;
		try {
			await onDelete();
		} finally {
			deleting = false;
		}
	}

	async function handleRedenominate(targetPrice: number) {
		await onRedenominate(targetPrice);
		redenominating = false;
	}
</script>

<div class="flex items-center justify-between p-3 bg-surface-2 rounded-lg" data-testid="alert-row">
	<div class="flex items-center gap-3 min-w-0">
		{#if onSelectChange}
			<input
				type="checkbox"
				checked={selected}
				onchange={(e) => onSelectChange(e.currentTarget.checked)}
				aria-label="Select alert for {product?.name ?? 'this product'}"
				class="h-4 w-4 shrink-0 rounded border-gray-300 dark:border-gray-600 text-brand focus:ring-brand"
			/>
		{/if}
		<div
			class="p-2 rounded-full shrink-0 {alert.hasCurrencyMismatch
				? 'bg-amber-100 dark:bg-amber-900/40'
				: alert.active
					? 'bg-yellow-100 dark:bg-yellow-900'
					: 'bg-gray-200 dark:bg-gray-600'}"
		>
			{#if alert.hasCurrencyMismatch}
				<TriangleAlert size={16} class="text-amber-600 dark:text-amber-400" />
			{:else}
				<Bell
					size={16}
					class={alert.active ? 'text-yellow-600 dark:text-yellow-400' : 'text-gray-400'}
				/>
			{/if}
		</div>
		<div class="min-w-0">
			{#if product}
				<a
					href={resolve(`/products/${product.id}`)}
					class="block text-sm font-medium text-gray-900 dark:text-white hover:text-brand dark:hover:text-brand-muted truncate"
				>
					{product.name}
				</a>
			{/if}
			<p
				class="text-sm tabular-nums {product
					? 'text-gray-600 dark:text-gray-300'
					: 'font-medium text-gray-900 dark:text-white'}"
			>
				{conditionLabels[alert.condition] ?? alert.condition}: {alert.condition === 'percentDrop'
					? `${alert.targetPrice.toFixed(0)}%`
					: formatPrice(alert.targetPrice, alert.currency)}
			</p>
			{#if alert.hasCurrencyMismatch}
				<p data-testid="alert-currency-mismatch" class="text-xs text-amber-700 dark:text-amber-400">
					Dormant — target is in {alert.currency}, product is now priced in
					{productCurrency}, so it won't trigger.
				</p>
				{#if redenominating}
					<RedenominateAlertForm
						oldCurrency={alert.currency}
						currency={productCurrency}
						currentPrice={productCurrentPrice}
						onSubmit={handleRedenominate}
						onCancel={() => (redenominating = false)}
					/>
				{:else}
					<button
						type="button"
						data-testid="redenominate-alert"
						onclick={() => (redenominating = true)}
						class="mt-1 text-xs font-medium text-amber-700 dark:text-amber-400 underline hover:no-underline"
					>
						Set a new target in {productCurrency}
					</button>
				{/if}
			{:else}
				<p class="text-xs text-gray-500 dark:text-gray-400">
					{alert.active ? 'Active' : 'Paused'}
					{#if alert.lastTriggered}
						&middot; Last triggered {new Date(alert.lastTriggered).toLocaleDateString()}
					{/if}
				</p>
			{/if}
		</div>
	</div>
	<div class="flex items-center gap-1 shrink-0">
		{#if onToggleActive}
			<button
				onclick={handleToggle}
				disabled={toggling}
				aria-label={alert.active ? 'Pause alert' : 'Resume alert'}
				title={alert.active ? 'Pause alert' : 'Resume alert'}
				class="p-2 text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-md disabled:opacity-50"
			>
				{#if toggling}
					<LoaderCircle size={16} class="animate-spin" />
				{:else if alert.active}
					<Pause size={16} />
				{:else}
					<Play size={16} />
				{/if}
			</button>
		{/if}
		<button
			onclick={handleDelete}
			disabled={deleting}
			aria-label="Delete alert"
			class="p-2 text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-md disabled:opacity-50"
		>
			{#if deleting}
				<LoaderCircle size={16} class="animate-spin" />
			{:else}
				<Trash2 size={16} />
			{/if}
		</button>
	</div>
</div>
