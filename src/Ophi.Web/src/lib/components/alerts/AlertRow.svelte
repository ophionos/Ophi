<script lang="ts">
	import { resolve } from '$app/paths';
	import { Bell, LoaderCircle, Pause, Play, Trash2, TriangleAlert } from 'lucide-svelte';
	import type { ProductAlert } from '$lib/api/client';
	import { formatPrice, formatDate } from '$lib/format';
	import RedenominateAlertForm from '$lib/components/alerts/RedenominateAlertForm.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';

	interface Props {
		alert: ProductAlert;
		/** The product's current currency — what a dormant alert must be re-denominated into. */
		productCurrency: string;
		productCurrentPrice?: number;
		/** The price before the product's last change — the baseline a percent-drop alert fires against. */
		productPreviousPrice?: number;
		/** Shown as a thumbnail when the row names its product. */
		productImageUrl?: string;
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
		productPreviousPrice,
		productImageUrl,
		product,
		onDelete,
		onRedenominate,
		onToggleActive,
		onSelectChange,
		selected = false
	}: Props = $props();

	let deleting = $state(false);
	let confirmingDelete = $state(false);
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

	// Deleting also drops the alert's trigger history, so it is confirmed like other destructive actions.
	async function handleConfirmDelete() {
		deleting = true;
		try {
			await onDelete();
		} finally {
			deleting = false;
			confirmingDelete = false;
		}
	}

	// Not dormant means the target and the product price share productCurrency, so they compare.
	// A percent-drop alert fires on one change between two checks, so it has no standing distance:
	// show the last change instead.
	const distance = $derived.by(() => {
		if (alert.hasCurrencyMismatch || productCurrentPrice == null) return null;
		const now = `${formatPrice(productCurrentPrice, productCurrency)} now`;
		if (alert.condition === 'percentDrop') {
			if (!productPreviousPrice || productPreviousPrice === productCurrentPrice) return { text: now, reached: false };
			const change = ((productCurrentPrice - productPreviousPrice) / productPreviousPrice) * 100;
			const pct = Math.abs(change).toFixed(1).replace(/\.0$/, '');
			const sign = change < 0 ? '−' : '+';
			return {
				text: `${now} · last change ${sign}${pct}% from ${formatPrice(productPreviousPrice, productCurrency)}`,
				reached: false
			};
		}
		const gap = alert.condition === 'below' ? productCurrentPrice - alert.targetPrice : alert.targetPrice - productCurrentPrice;
		if (gap <= 0) return { text: `${now} · target reached`, reached: true };
		const side = alert.condition === 'below' ? 'above' : 'below';
		return { text: `${now} · ${formatPrice(gap, productCurrency)} ${side} target`, reached: false };
	});

	async function handleRedenominate(targetPrice: number) {
		await onRedenominate(targetPrice);
		redenominating = false;
	}
</script>

<div
	class="flex items-center justify-between p-3 bg-surface-1 border border-gray-200 dark:border-gray-700/50 rounded-lg"
	data-testid="alert-row"
>
	<div class="flex items-center gap-3 min-w-0">
		{#if onSelectChange}
			<label class="-my-2 -ml-2 flex min-h-11 min-w-11 shrink-0 cursor-pointer items-center justify-center">
				<input
					type="checkbox"
					checked={selected}
					onchange={(e) => onSelectChange(e.currentTarget.checked)}
					aria-label="Select alert for {product?.name ?? 'this product'}"
					class="h-4 w-4"
				/>
			</label>
		{/if}
		{#if product}
			<div data-testid="alert-thumbnail" class="shrink-0">
				{#if productImageUrl}
					<img src={productImageUrl} alt={product.name} loading="lazy" class="h-10 w-10 rounded object-cover" />
				{:else}
					<div
						class="h-10 w-10 rounded bg-gray-100 dark:bg-gray-700 flex items-center justify-center text-sm font-semibold text-gray-500 dark:text-gray-400"
						aria-hidden="true"
					>
						{product.name.charAt(0).toUpperCase()}
					</div>
				{/if}
			</div>
		{/if}
		<div
			data-testid="alert-state-icon"
			class="p-2 rounded-full shrink-0 {alert.hasCurrencyMismatch
				? 'bg-warning/15'
				: alert.active
					? 'bg-brand-light dark:bg-brand-dark/30'
					: 'bg-gray-200 dark:bg-gray-600'}"
		>
			{#if alert.hasCurrencyMismatch}
				<TriangleAlert size={16} class="text-warning" />
			{:else}
				<Bell
					size={16}
					class={alert.active ? 'text-brand dark:text-brand-muted' : 'text-gray-400'}
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
			{#if distance}
				<p
					data-testid="alert-distance"
					class="text-xs tabular-nums {distance.reached
						? 'font-medium text-success'
						: 'text-gray-600 dark:text-gray-300'}"
					title={alert.condition === 'percentDrop'
						? `Fires when one price change between two checks is a drop of ${alert.targetPrice.toFixed(0)}% or more`
						: undefined}
				>
					{distance.text}
				</p>
			{/if}
			{#if alert.hasCurrencyMismatch}
				<p data-testid="alert-currency-mismatch" class="text-xs text-warning">
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
						class="mt-1 text-xs font-medium text-warning underline hover:no-underline"
					>
						Set a new target in {productCurrency}
					</button>
				{/if}
			{:else}
				<p class="text-xs text-gray-500 dark:text-gray-400">
					{alert.active ? 'Active' : 'Paused'}
					{#if alert.lastTriggered}
						&middot; Last triggered {formatDate(alert.lastTriggered)}
					{/if}
				</p>
			{/if}
		</div>
	</div>
	<div class="flex items-center gap-2 shrink-0">
		{#if onToggleActive}
			<button
				onclick={handleToggle}
				disabled={toggling}
				aria-label={alert.active ? 'Pause alert' : 'Resume alert'}
				title={alert.active ? 'Pause alert' : 'Resume alert'}
				class="flex min-h-11 min-w-11 items-center justify-center gap-1 px-2 text-sm text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-md disabled:opacity-50"
			>
				{#if toggling}
					<LoaderCircle size={16} class="animate-spin" />
				{:else if alert.active}
					<Pause size={16} />
				{:else}
					<Play size={16} />
				{/if}
				<span class="hidden sm:inline">{alert.active ? 'Pause' : 'Resume'}</span>
			</button>
		{/if}
		<button
			onclick={() => (confirmingDelete = true)}
			disabled={deleting}
			aria-label="Delete alert"
			title="Delete alert"
			class="flex min-h-11 min-w-11 items-center justify-center gap-1 px-2 text-sm text-gray-500 dark:text-gray-400 hover:text-danger hover:bg-danger/10 rounded-md disabled:opacity-50"
		>
			{#if deleting}
				<LoaderCircle size={16} class="animate-spin" />
			{:else}
				<Trash2 size={16} />
			{/if}
			<span class="hidden sm:inline">Delete</span>
		</button>
	</div>
</div>

<ConfirmModal
	isOpen={confirmingDelete}
	title="Delete alert"
	message="Delete this alert{product
		? ` for ${product.name}`
		: ''} and its trigger history? This cannot be undone."
	loading={deleting}
	onConfirm={handleConfirmDelete}
	onCancel={() => (confirmingDelete = false)}
/>
