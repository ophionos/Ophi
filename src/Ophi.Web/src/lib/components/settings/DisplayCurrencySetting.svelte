<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { fx } from '$lib/stores/fx.svelte';
	import { toast } from '$lib/stores/toast.svelte';

	let supported = $state<string[]>([]);
	let saving = $state(false);

	onMount(async () => {
		try {
			// The server owns the list, so the picker can never offer a currency the validator rejects.
			const [, rates] = await Promise.all([fx.load(), api.getFxRates()]);
			supported = rates.supported;
		} catch {
			toast.error('Failed to load currencies');
		}
	});

	async function handleChange(e: Event) {
		const value = (e.currentTarget as HTMLSelectElement).value;
		saving = true;
		try {
			await api.updateSettings({ displayCurrency: value });
			await fx.setDisplayCurrency(value || null);
			toast.success(value ? `Prices will also show in ${value}` : 'Currency conversion turned off');
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
			<label for="display-currency" class="text-sm font-medium text-gray-900 dark:text-white">
				Display currency
			</label>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Also show prices converted to this currency. The store's own price stays primary.
			</p>
		</div>
		<select
			id="display-currency"
			value={fx.displayCurrency ?? ''}
			onchange={handleChange}
			disabled={saving || supported.length === 0}
			class="shrink-0 px-2 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
		>
			<option value="">Off</option>
			{#each supported as code (code)}
				<option value={code}>{code}</option>
			{/each}
		</select>
	</div>
	<p class="text-xs text-gray-400 dark:text-gray-500">
		Uses the European Central Bank's daily reference rates. Conversions are approximate and are
		never used for alerts or comparisons.
	</p>
</div>
