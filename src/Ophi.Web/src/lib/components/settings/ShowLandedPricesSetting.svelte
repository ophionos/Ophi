<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';

	let enabled = $state(false);
	let loaded = $state(false);
	let saving = $state(false);

	// The comparison and product pages read the setting in their loaders, so a change here shows
	// up the next time one of them loads.
	onMount(async () => {
		try {
			enabled = (await api.getSettings()).showLandedPrices ?? false;
			loaded = true;
		} catch {
			toast.error('Failed to load settings');
		}
	});

	async function handleToggle() {
		const next = !enabled;
		saving = true;
		try {
			await api.updateSettings({ showLandedPrices: next });
			enabled = next;
			toast.success(next ? 'Comparisons now include shipping' : 'Comparisons use the store price');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
		} finally {
			saving = false;
		}
	}
</script>

<div
	class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-2"
>
	<div class="flex items-center justify-between gap-3">
		<div class="min-w-0">
			<p id="show-landed-prices-label" class="text-sm font-medium text-gray-900 dark:text-white">
				Show prices with shipping
			</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Rank and show comparison prices including the shipping cost you set per store. Alerts and
				price history stay on the store's price.
			</p>
		</div>
		<button
			type="button"
			onclick={handleToggle}
			disabled={saving || !loaded}
			class="tap-area inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors {enabled
				? 'bg-brand'
				: 'bg-gray-300 dark:bg-gray-600'} disabled:opacity-50"
			role="switch"
			aria-checked={enabled}
			aria-labelledby="show-landed-prices-label"
			data-testid="show-landed-prices-toggle"
		>
			<span
				class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {enabled
					? 'translate-x-6'
					: 'translate-x-1'}"
			></span>
		</button>
	</div>
</div>
