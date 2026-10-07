<script lang="ts">
	import { LoaderCircle } from 'lucide-svelte';
	import { ApiError } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		/** The currency the alert's target is stuck in — the one that went dormant. */
		oldCurrency: string;
		/** The product's current currency. The new target is denominated in this. */
		currency: string;
		currentPrice?: number;
		onSubmit: (targetPrice: number) => Promise<void>;
		onCancel: () => void;
	}

	let { oldCurrency, currency, currentPrice, onSubmit, onCancel }: Props = $props();

	// Seeded from the product's current price rather than the old target: the old number is an
	// amount in oldCurrency, so offering it here would invite exactly the meaningless carry-over
	// this form exists to avoid.
	let targetPrice = $derived(currentPrice ? Math.floor(currentPrice * 0.9) : 0);
	let loading = $state(false);
	let error = $state('');

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!targetPrice || targetPrice <= 0) {
			error = 'Target price must be greater than 0';
			return;
		}

		loading = true;
		error = '';

		try {
			await onSubmit(targetPrice);
		} catch (err) {
			if (err instanceof ApiError) {
				error = err.message;
			} else {
				error = err instanceof Error ? err.message : 'An error occurred';
			}
		} finally {
			loading = false;
		}
	}
</script>

<form onsubmit={handleSubmit} class="mt-2 space-y-2" data-testid="redenominate-form">
	<p class="text-xs text-gray-500 dark:text-gray-400">
		Your old target was in {oldCurrency}, so it can't carry over. Set a new one in {currency}.
		{#if currentPrice}
			Currently {formatPrice(currentPrice, currency)}.
		{/if}
	</p>

	{#if error}
		<FormError size="xs" testid="redenominate-error" message={error} />
	{/if}

	<div class="flex items-center gap-2">
		<div class="relative rounded-md">
			<div class="absolute inset-y-0 left-0 pl-2 flex items-center pointer-events-none">
				<span class="text-gray-500 dark:text-gray-400 text-xs">{currency}</span>
			</div>
			<input
				type="number"
				step="0.01"
				min="0"
				aria-label="New target price in {currency}"
				bind:value={targetPrice}
				disabled={loading}
				class="block w-32 pl-11 pr-2 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-white dark:bg-gray-700 text-gray-900 dark:text-white"
			/>
		</div>
		<button
			type="submit"
			disabled={loading}
			class="px-3 py-1 text-xs font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 flex items-center gap-1"
		>
			{#if loading}
				<LoaderCircle size={14} class="animate-spin" />
			{/if}
			Set target
		</button>
		<button
			type="button"
			onclick={onCancel}
			disabled={loading}
			class="px-3 py-1 text-xs font-medium text-gray-700 dark:text-gray-300 hover:underline disabled:opacity-50"
		>
			Cancel
		</button>
	</div>
</form>
