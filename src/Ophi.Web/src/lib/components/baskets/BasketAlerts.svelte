<script lang="ts">
	import { LoaderCircle, Pause, Play, Trash2, TriangleAlert } from 'lucide-svelte';
	import { api, type BasketAlert, type BasketStore } from '$lib/api/client';
	import { formatPrice, formatDate } from '$lib/format';
	import { toast } from '$lib/stores/toast.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		basketId: string;
		alerts: BasketAlert[];
		/** Only complete stores have a total an alert can watch. */
		stores: BasketStore[];
	}

	let { basketId, alerts: initialAlerts, stores }: Props = $props();

	// A writable $derived: actions replace it, a new load resets it.
	let alerts = $derived(initialAlerts);
	const hosts = $derived(stores.filter((s) => s.complete && s.total != null).map((s) => s.host));

	let target = $state<number | null>(null);
	let host = $state('');
	let creating = $state(false);
	let createError = $state<string | null>(null);
	let busyId = $state<string | null>(null);
	let deleting = $state<BasketAlert | null>(null);

	async function handleCreate(event: SubmitEvent) {
		event.preventDefault();
		const targetTotal = Number(target);
		createError = null;
		if (!(targetTotal > 0)) {
			createError = 'The target total must be greater than 0.';
			return;
		}
		creating = true;
		try {
			const created = await api.createBasketAlert(basketId, { targetTotal, host: host || null });
			alerts = [...alerts, created];
			target = null;
		} catch (err) {
			createError = err instanceof Error ? err.message : 'Failed to create the alert';
		} finally {
			creating = false;
		}
	}

	async function handleToggle(alert: BasketAlert) {
		busyId = alert.id;
		try {
			const updated = await api.setBasketAlertActive(basketId, alert.id, !alert.active);
			alerts = alerts.map((a) => (a.id === updated.id ? updated : a));
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update the alert');
		} finally {
			busyId = null;
		}
	}

	async function handleConfirmDelete() {
		if (!deleting) return;
		const id = deleting.id;
		busyId = id;
		try {
			await api.deleteBasketAlert(basketId, id);
			alerts = alerts.filter((a) => a.id !== id);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to delete the alert');
		} finally {
			busyId = null;
			deleting = null;
		}
	}
</script>

<div class="space-y-4">
	<p class="text-sm text-gray-500 dark:text-gray-400">
		An alert fires when the basket total is at or under the target. The check runs when an item's
		price changes. A quantity or shipping-cost change is checked at the next price change.
	</p>

	{#if alerts.length > 0}
		<ul class="space-y-2">
			{#each alerts as alert (alert.id)}
				<li
					class="flex items-center justify-between gap-3 p-3 bg-surface-1 border border-gray-200 dark:border-gray-700/50 rounded-lg"
					data-testid="basket-alert-row"
				>
					<div class="min-w-0">
						<p class="text-sm font-medium text-gray-900 dark:text-white">
							Under {formatPrice(alert.targetTotal, alert.currency)} at {alert.host ?? 'the best store'}
						</p>
						{#if alert.hasCurrencyMismatch}
							<p class="flex items-center gap-1 text-xs text-warning">
								<TriangleAlert size={14} />
								The total is now in another currency ({alert.watchedCurrency}). This alert cannot fire.
							</p>
						{:else if alert.watchedTotal != null && alert.watchedCurrency && alert.watchedHost}
							<p class="text-xs text-gray-600 dark:text-gray-300">
								{formatPrice(alert.watchedTotal, alert.watchedCurrency)} now at {alert.watchedHost}
							</p>
						{:else}
							<p class="text-xs text-gray-500 dark:text-gray-400">No store has a total to compare now.</p>
						{/if}
						<p class="text-xs text-gray-500 dark:text-gray-400">
							{alert.active ? 'Active' : 'Paused'}
							{#if alert.lastTriggered}
								&middot; Last triggered {formatDate(alert.lastTriggered)}
							{/if}
						</p>
					</div>
					<div class="flex items-center gap-2 shrink-0">
						<button
							onclick={() => handleToggle(alert)}
							disabled={busyId === alert.id}
							aria-label={alert.active ? 'Pause alert' : 'Resume alert'}
							title={alert.active ? 'Pause alert' : 'Resume alert'}
							class="flex min-h-11 min-w-11 items-center justify-center gap-1 px-2 text-sm text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-md disabled:opacity-50"
						>
							{#if busyId === alert.id}
								<LoaderCircle size={16} class="animate-spin" />
							{:else if alert.active}
								<Pause size={16} />
							{:else}
								<Play size={16} />
							{/if}
							<span class="hidden sm:inline">{alert.active ? 'Pause' : 'Resume'}</span>
						</button>
						<button
							onclick={() => (deleting = alert)}
							disabled={busyId === alert.id}
							aria-label="Delete alert"
							title="Delete alert"
							class="flex min-h-11 min-w-11 items-center justify-center gap-1 px-2 text-sm text-gray-500 dark:text-gray-400 hover:text-danger hover:bg-danger/10 rounded-md disabled:opacity-50"
						>
							<Trash2 size={16} />
							<span class="hidden sm:inline">Delete</span>
						</button>
					</div>
				</li>
			{/each}
		</ul>
	{/if}

	<form onsubmit={handleCreate} class="flex flex-wrap items-end gap-3">
		<label class="flex flex-col gap-1 text-sm text-gray-700 dark:text-gray-300">
			Target total
			<input
				type="number"
				step="0.01"
				bind:value={target}
				class="w-36 px-3 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md bg-surface-field text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent"
			/>
		</label>
		<label class="flex flex-col gap-1 text-sm text-gray-700 dark:text-gray-300">
			Store
			<select
				bind:value={host}
				class="px-3 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md bg-surface-field text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent"
			>
				<option value="">The best store</option>
				{#each hosts as h (h)}
					<option value={h}>{h}</option>
				{/each}
			</select>
		</label>
		<button
			type="submit"
			disabled={creating}
			class="px-4 min-h-11 bg-brand text-white rounded-lg hover:bg-brand-hover disabled:opacity-50 flex items-center gap-2"
		>
			{#if creating}
				<LoaderCircle size={16} class="animate-spin" />
			{/if}
			Add alert
		</button>
	</form>
	{#if createError}
		<FormError message={createError} />
	{/if}
</div>

<ConfirmModal
	isOpen={deleting !== null}
	title="Delete alert"
	message="Delete this basket alert and its trigger history? This cannot be undone."
	loading={busyId !== null && busyId === deleting?.id}
	onConfirm={handleConfirmDelete}
	onCancel={() => (deleting = null)}
/>
