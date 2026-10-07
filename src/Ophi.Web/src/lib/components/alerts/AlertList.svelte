<script lang="ts">
	import type { ProductAlert } from '$lib/api/client';
	import AlertRow from '$lib/components/alerts/AlertRow.svelte';

	interface Props {
		alerts: ProductAlert[];
		productCurrency: string;
		productCurrentPrice?: number;
		onDelete: (alertId: string) => Promise<void>;
		onRedenominate: (alertId: string, targetPrice: number) => Promise<void>;
		onToggleActive?: (alertId: string) => Promise<void>;
	}

	let { alerts, productCurrency, productCurrentPrice, onDelete, onRedenominate, onToggleActive }: Props =
		$props();
</script>

{#if alerts.length === 0}
	<div class="text-center py-4" data-testid="no-alerts-tip">
		<p class="text-gray-500 dark:text-gray-400">No alerts set for this product</p>
		<p class="text-xs text-gray-400 dark:text-gray-500 mt-1">
			Click "Create Alert" to get notified when the price reaches your target.
		</p>
	</div>
{:else}
	<div class="space-y-3">
		{#each alerts as alert (alert.id)}
			<AlertRow
				{alert}
				{productCurrency}
				{productCurrentPrice}
				onDelete={() => onDelete(alert.id)}
				onRedenominate={(targetPrice) => onRedenominate(alert.id, targetPrice)}
				onToggleActive={onToggleActive ? () => onToggleActive(alert.id) : undefined}
			/>
		{/each}
	</div>
{/if}
