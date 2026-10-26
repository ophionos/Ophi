<script lang="ts">
	import Modal from '$lib/components/shared/Modal.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';
	import { api, ApiError, type ShippingRate } from '$lib/api/client';
	import { fx } from '$lib/stores/fx.svelte';
	import { shippingHost } from '$lib/utils/shipping';

	interface Props {
		isOpen: boolean;
		/** Set to edit an existing rate: the host is then fixed. */
		rate?: ShippingRate | null;
		/** Starting values for a new rate (e.g. from a product URL). */
		prefill?: { host?: string; currency?: string } | null;
		onClose: () => void;
		onSaved: (rate: ShippingRate) => void;
	}

	let { isOpen, rate = null, prefill = null, onClose, onSaved }: Props = $props();

	let host = $state('');
	let amount = $state<number | null>(null);
	let currency = $state('EUR');
	let freeAbove = $state<number | null>(null);
	let saving = $state(false);
	let formError = $state('');
	let fieldErrors = $state<Record<'host' | 'amount' | 'currency' | 'freeabove', string[]>>({
		host: [],
		amount: [],
		currency: [],
		freeabove: []
	});

	const isEditing = $derived(rate !== null);

	$effect(() => {
		if (!isOpen) return;
		if (rate) {
			host = rate.host;
			amount = rate.amount;
			currency = rate.currency;
			freeAbove = rate.freeAbove ?? null;
		} else {
			host = prefill?.host ? shippingHost(prefill.host) : '';
			amount = null;
			currency = prefill?.currency ?? fx.displayCurrency ?? 'EUR';
			freeAbove = null;
		}
		saving = false;
		formError = '';
		fieldErrors = { host: [], amount: [], currency: [], freeabove: [] };
	});

	async function handleSubmit(e: Event) {
		e.preventDefault();
		formError = '';
		fieldErrors = { host: [], amount: [], currency: [], freeabove: [] };

		// A number input binds null when empty.
		if (amount === null || !Number.isFinite(amount) || amount < 0) {
			formError = 'Enter a shipping cost (0 or more)';
			return;
		}

		saving = true;
		try {
			const saved = await api.putShippingRate(host.trim(), {
				amount,
				currency: currency.trim(),
				freeAbove: freeAbove ?? null
			});
			onSaved(saved);
		} catch (err) {
			if (err instanceof ApiError && err.hasFieldErrors()) {
				fieldErrors = {
					host: err.getFieldErrors('host'),
					amount: err.getFieldErrors('amount'),
					currency: err.getFieldErrors('currency'),
					freeabove: err.getFieldErrors('freeAbove')
				};
			} else {
				formError = err instanceof Error ? err.message : 'Failed to save shipping rate';
			}
		} finally {
			saving = false;
		}
	}

	const inputClass =
		'w-full px-3 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-surface-field text-gray-900 dark:text-white read-only:opacity-60';
	const labelClass = 'block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1';
</script>

<Modal
	{isOpen}
	title={isEditing ? 'Edit shipping cost' : 'Set shipping cost'}
	size="sm"
	closeDisabled={saving}
	{onClose}
>
	<form onsubmit={handleSubmit} class="flex-1 overflow-y-auto p-6 space-y-4">
		<p class="text-xs text-gray-500 dark:text-gray-400">
			A flat cost added to prices from this site. It is never converted: it only applies to prices
			in the same currency.
		</p>

		{#if formError}
			<FormError variant="box" message={formError} />
		{/if}

		<div>
			<label for="shipping-host" class={labelClass}>Site host</label>
			<input
				id="shipping-host"
				type="text"
				bind:value={host}
				readonly={isEditing}
				placeholder="e.g. amazon.es"
				class={inputClass}
				data-testid="shipping-host-input"
			/>
			{#each fieldErrors.host as message (message)}
				<FormError class="mt-1" {message} />
			{/each}
		</div>

		<div class="grid grid-cols-2 gap-3">
			<div>
				<label for="shipping-amount" class={labelClass}>Shipping cost</label>
				<input
					id="shipping-amount"
					type="number"
					step="0.01"
					min="0"
					bind:value={amount}
					class={inputClass}
					data-testid="shipping-amount-input"
				/>
				{#each fieldErrors.amount as message (message)}
					<FormError class="mt-1" {message} />
				{/each}
			</div>
			<div>
				<label for="shipping-currency" class={labelClass}>Currency</label>
				<input
					id="shipping-currency"
					type="text"
					maxlength={3}
					bind:value={currency}
					class="{inputClass} uppercase"
					data-testid="shipping-currency-input"
				/>
				{#each fieldErrors.currency as message (message)}
					<FormError class="mt-1" {message} />
				{/each}
			</div>
		</div>

		<div>
			<label for="shipping-free-above" class={labelClass}>Free above (optional)</label>
			<input
				id="shipping-free-above"
				type="number"
				step="0.01"
				min="0"
				bind:value={freeAbove}
				placeholder="No free-shipping threshold"
				class={inputClass}
				data-testid="shipping-free-above-input"
			/>
			{#each fieldErrors.freeabove as message (message)}
				<FormError class="mt-1" {message} />
			{/each}
		</div>

		<div class="flex justify-end gap-3 pt-2">
			<button
				type="button"
				onclick={onClose}
				disabled={saving}
				class="px-4 min-h-11 text-sm font-medium text-gray-700 dark:text-gray-300 bg-surface-field border border-gray-300 dark:border-gray-600 rounded-md hover:bg-surface-field-hover disabled:opacity-50"
			>
				Cancel
			</button>
			<button
				type="submit"
				disabled={saving}
				class="px-4 min-h-11 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50"
				data-testid="shipping-save"
			>
				{saving ? 'Saving...' : 'Save'}
			</button>
		</div>
	</form>
</Modal>
