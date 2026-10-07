<script lang="ts">
	import type { PageData } from './$types';
	import { page } from '$app/state';
	import { replaceState } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { api, type Alert } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';
	import { liveRefresh } from '$lib/utils/liveRefresh';
	import AlertRow from '$lib/components/alerts/AlertRow.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import { Bell, Pause, Play, Trash2 } from 'lucide-svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	let { data }: { data: PageData } = $props();

	// Writable derived: seeded from the loader, then updated locally by mutations and live refresh.
	let alerts = $derived<Alert[]>(data.alerts);

	type StatusFilter = 'all' | 'active' | 'paused' | 'dormant' | 'recent';
	type SortKey = 'fired' | 'product' | 'created';

	const STATUS_FILTERS: { key: StatusFilter; label: string }[] = [
		{ key: 'all', label: 'All' },
		{ key: 'active', label: 'Active' },
		{ key: 'paused', label: 'Paused' },
		{ key: 'dormant', label: 'Dormant' },
		{ key: 'recent', label: 'Fired in last 7 days' }
	];
	const SORTS: { key: SortKey; label: string }[] = [
		{ key: 'fired', label: 'Last fired' },
		{ key: 'product', label: 'Product name' },
		{ key: 'created', label: 'Newest first' }
	];
	const RECENT_MS = 7 * 24 * 60 * 60 * 1000;

	// Filter state starts from the URL so refreshes and shared links land on the same view, and
	// every change is mirrored back with a shallow replaceState (no loader re-run).
	const initParams = page.url.searchParams;
	const initStatus = initParams.get('status');
	const initSort = initParams.get('sort');
	let status = $state<StatusFilter>(
		STATUS_FILTERS.some((f) => f.key === initStatus) ? (initStatus as StatusFilter) : 'all'
	);
	let sort = $state<SortKey>(
		SORTS.some((s) => s.key === initSort) ? (initSort as SortKey) : 'fired'
	);

	function syncUrl() {
		const url = new URL(page.url);
		const sp = url.searchParams;
		if (status === 'all') sp.delete('status');
		else sp.set('status', status);
		if (sort === 'fired') sp.delete('sort');
		else sp.set('sort', sort);
		replaceState(url, {});
	}

	function setStatus(next: StatusFilter) {
		status = next;
		selectedIds = new Set();
		syncUrl();
	}

	function setSort(next: SortKey) {
		sort = next;
		syncUrl();
	}

	// "Active" means it can fire now — a dormant alert is flagged active but cannot trigger.
	function matches(alert: Alert, filter: StatusFilter): boolean {
		switch (filter) {
			case 'active':
				return alert.active && !alert.hasCurrencyMismatch;
			case 'paused':
				return !alert.active;
			case 'dormant':
				return alert.hasCurrencyMismatch;
			case 'recent':
				return !!alert.lastTriggered && Date.now() - Date.parse(alert.lastTriggered) < RECENT_MS;
			default:
				return true;
		}
	}

	const counts = $derived(
		Object.fromEntries(
			STATUS_FILTERS.map((f) => [f.key, alerts.filter((a) => matches(a, f.key)).length])
		) as Record<StatusFilter, number>
	);

	// The API returns newest-created first, so 'created' keeps server order.
	const visible = $derived.by(() => {
		const filtered = alerts.filter((a) => matches(a, status));
		if (sort === 'product') {
			return [...filtered].sort((a, b) => a.productName.localeCompare(b.productName));
		}
		if (sort === 'fired') {
			const firedAt = (a: Alert) => (a.lastTriggered ? Date.parse(a.lastTriggered) : -Infinity);
			return [...filtered].sort((a, b) => firedAt(b) - firedAt(a));
		}
		return filtered;
	});

	async function refresh() {
		try {
			alerts = (await api.getAlerts()).items;
		} catch {
			// transient — keep what we have; the next push or poll retries
		}
	}

	// A firing alert publishes a `notification` event after LastTriggeredAt is stamped.
	$effect(() =>
		liveRefresh({ matches: (evt) => evt.type === 'notification', refresh, intervalMs: 30000 })
	);

	function replace(updated: Alert) {
		alerts = alerts.map((a) => (a.id === updated.id ? updated : a));
	}

	async function handleToggle(alert: Alert) {
		try {
			replace(await api.setAlertActive(alert.id, !alert.active));
			toast.success(alert.active ? 'Alert paused' : 'Alert resumed');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update alert');
		}
	}

	async function handleDelete(alert: Alert) {
		try {
			await api.deleteAlert(alert.id);
			alerts = alerts.filter((a) => a.id !== alert.id);
			toast.success('Alert deleted');
		} catch {
			toast.error('Failed to delete alert');
		}
	}

	async function handleRedenominate(alert: Alert, targetPrice: number) {
		// Send the currency the user was shown; the server refuses if the product moved since.
		replace(await api.redenominateAlert(alert.id, targetPrice, alert.productCurrency));
		toast.success('Alert target updated');
	}

	// --- Bulk selection ---
	let selectedIds = $state<ReadonlySet<string>>(new Set());
	let bulkBusy = $state(false);
	let bulkDeleteConfirmOpen = $state(false);

	const selectedVisible = $derived(visible.filter((a) => selectedIds.has(a.id)));
	const allVisibleSelected = $derived(
		visible.length > 0 && visible.every((a) => selectedIds.has(a.id))
	);

	function setSelected(id: string, on: boolean) {
		const next = new Set(selectedIds);
		if (on) next.add(id);
		else next.delete(id);
		selectedIds = next;
	}

	function toggleSelectAll() {
		selectedIds = allVisibleSelected ? new Set() : new Set(visible.map((a) => a.id));
	}

	async function bulkSetActive(active: boolean) {
		const targets = selectedVisible;
		if (targets.length === 0) return;
		bulkBusy = true;
		const results = await Promise.allSettled(
			targets.map((a) => api.setAlertActive(a.id, active))
		);
		bulkBusy = false;
		for (const r of results) if (r.status === 'fulfilled') replace(r.value);
		const failed = results.filter((r) => r.status === 'rejected').length;
		if (failed > 0) {
			toast.error(`${failed} of ${targets.length} alerts failed to update`);
		} else {
			toast.success(
				`${targets.length} alert${targets.length === 1 ? '' : 's'} ${active ? 'resumed' : 'paused'}`
			);
		}
		selectedIds = new Set();
	}

	async function confirmBulkDelete() {
		const targets = selectedVisible;
		if (targets.length === 0) return;
		bulkBusy = true;
		const results = await Promise.allSettled(targets.map((a) => api.deleteAlert(a.id)));
		bulkBusy = false;
		bulkDeleteConfirmOpen = false;
		const deleted = new Set(
			targets.filter((_, i) => results[i].status === 'fulfilled').map((a) => a.id)
		);
		alerts = alerts.filter((a) => !deleted.has(a.id));
		const failed = targets.length - deleted.size;
		if (failed > 0) {
			toast.error(`${failed} of ${targets.length} alerts failed to delete`);
		} else {
			toast.success(`${targets.length} alert${targets.length === 1 ? '' : 's'} deleted`);
		}
		selectedIds = new Set();
	}
</script>

<svelte:head>
	<title>Alerts - Ophi</title>
</svelte:head>

<div>
	<div class="mb-6">
		<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">Alerts</h1>
		<p class="text-gray-600 dark:text-gray-400">Every price alert across your products</p>
	</div>

	{#if alerts.length === 0}
		<EmptyState
			icon={Bell}
			title="No alerts yet"
			description='Open a product and choose "Create Alert" to get notified when its price moves.'
			testid="alerts-empty"
		>
			<a
				href={resolve('/dashboard')}
				class="inline-flex items-center px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
			>
				Go to dashboard
			</a>
		</EmptyState>
	{:else}
		<div class="flex flex-wrap items-center justify-between gap-3 mb-4">
			<div class="flex flex-wrap gap-2" role="group" aria-label="Filter alerts">
				{#each STATUS_FILTERS as f (f.key)}
					<button
						onclick={() => setStatus(f.key)}
						aria-pressed={status === f.key}
						class="px-3 py-1 text-sm rounded-full border transition-colors {status === f.key
							? 'bg-brand text-white border-brand'
							: 'border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 hover:bg-surface-2'}"
					>
						{f.label} <span class="tabular-nums opacity-75">({counts[f.key]})</span>
					</button>
				{/each}
			</div>
			<label class="flex items-center gap-2 text-sm text-gray-600 dark:text-gray-400">
				Sort
				<select
					value={sort}
					onchange={(e) => setSort(e.currentTarget.value as SortKey)}
					class="px-2 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
				>
					{#each SORTS as s (s.key)}
						<option value={s.key}>{s.label}</option>
					{/each}
				</select>
			</label>
		</div>

		{#if visible.length > 0}
			<div class="flex items-center gap-3 mb-3 px-3 min-h-9">
				<label class="flex items-center gap-2 text-sm text-gray-600 dark:text-gray-400">
					<input
						type="checkbox"
						checked={allVisibleSelected}
						onchange={toggleSelectAll}
						class="h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-brand focus:ring-brand"
					/>
					Select all
				</label>
				{#if selectedVisible.length > 0}
					<span class="text-sm text-gray-500 dark:text-gray-400" aria-live="polite">
						{selectedVisible.length} selected
					</span>
					<button
						onclick={() => bulkSetActive(false)}
						disabled={bulkBusy}
						class="flex items-center gap-1 px-2.5 py-1 text-sm rounded-md text-gray-700 dark:text-gray-300 hover:bg-surface-2 disabled:opacity-50"
					>
						<Pause size={16} /> Pause selected
					</button>
					<button
						onclick={() => bulkSetActive(true)}
						disabled={bulkBusy}
						class="flex items-center gap-1 px-2.5 py-1 text-sm rounded-md text-gray-700 dark:text-gray-300 hover:bg-surface-2 disabled:opacity-50"
					>
						<Play size={16} /> Resume selected
					</button>
					<button
						onclick={() => (bulkDeleteConfirmOpen = true)}
						disabled={bulkBusy}
						class="flex items-center gap-1 px-2.5 py-1 text-sm rounded-md text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 disabled:opacity-50"
					>
						<Trash2 size={16} /> Delete selected
					</button>
				{/if}
			</div>

			<div class="space-y-2">
				{#each visible as alert (alert.id)}
					<AlertRow
						{alert}
						product={{ id: alert.productId, name: alert.productName }}
						productCurrency={alert.productCurrency}
						productCurrentPrice={alert.currentPrice}
						selected={selectedIds.has(alert.id)}
						onSelectChange={(on) => setSelected(alert.id, on)}
						onToggleActive={() => handleToggle(alert)}
						onDelete={() => handleDelete(alert)}
						onRedenominate={(price) => handleRedenominate(alert, price)}
					/>
				{/each}
			</div>
		{:else}
			<p class="text-center py-10 text-gray-500 dark:text-gray-400">No alerts match this filter</p>
		{/if}
	{/if}
</div>

<ConfirmModal
	isOpen={bulkDeleteConfirmOpen}
	title="Delete alerts"
	message="Delete {selectedVisible.length} alert{selectedVisible.length === 1
		? ''
		: 's'}? This cannot be undone."
	loading={bulkBusy}
	onConfirm={confirmBulkDelete}
	onCancel={() => (bulkDeleteConfirmOpen = false)}
/>
