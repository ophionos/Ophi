<script lang="ts">
	import { Truck, Plus, Pencil, Trash2 } from 'lucide-svelte';
	import { api, type ShippingRate } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import { toast } from '$lib/stores/toast.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import ShippingRateModal from './ShippingRateModal.svelte';

	interface Props {
		rates: ShippingRate[];
	}

	let { rates: initialRates }: Props = $props();

	// Writable derived: follows the loader data, and local saves/deletes override it until the next load.
	let rates = $derived<ShippingRate[]>([...initialRates]);

	let modalOpen = $state(false);
	let editing = $state<ShippingRate | null>(null);
	let deleting = $state<ShippingRate | null>(null);
	let deleteLoading = $state(false);

	function openAdd() {
		editing = null;
		modalOpen = true;
	}

	function openEdit(rate: ShippingRate) {
		editing = rate;
		modalOpen = true;
	}

	function handleSaved(saved: ShippingRate) {
		rates = [...rates.filter((r) => r.host !== saved.host), saved].sort((a, b) =>
			a.host.localeCompare(b.host)
		);
		modalOpen = false;
		toast.success(`Shipping cost for ${saved.host} saved`);
	}

	async function handleConfirmDelete() {
		if (!deleting) return;
		const host = deleting.host;
		deleteLoading = true;
		try {
			await api.deleteShippingRate(host);
			rates = rates.filter((r) => r.host !== host);
			toast.success(`Shipping cost for ${host} removed`);
			deleting = null;
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to remove shipping cost');
		} finally {
			deleteLoading = false;
		}
	}
</script>

<section
	class="mb-6 bg-surface-1 rounded-lg border border-gray-200 dark:border-gray-700 p-4"
	data-testid="shipping-rates-section"
>
	<div class="flex items-center justify-between gap-3">
		<div class="min-w-0">
			<h2 class="flex items-center gap-2 text-sm font-semibold text-gray-900 dark:text-white">
				<Truck size={16} class="shrink-0" />
				Shipping costs
			</h2>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				A flat cost per site, added to its prices when "Show prices with shipping" is on.
			</p>
		</div>
		<button
			type="button"
			onclick={openAdd}
			class="shrink-0 whitespace-nowrap px-3 min-h-11 text-sm font-medium bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-200 dark:hover:bg-gray-600 flex items-center gap-1"
		>
			<Plus size={16} />
			Add shipping cost
		</button>
	</div>

	{#if rates.length === 0}
		<p class="mt-3 text-sm text-gray-500 dark:text-gray-400">
			No shipping costs set. Prices are compared without shipping.
		</p>
	{:else}
		<ul class="mt-3 divide-y divide-gray-200 dark:divide-gray-700">
			{#each rates as rate (rate.host)}
				<li class="flex items-center justify-between gap-3" data-testid="shipping-rate-row">
					<div class="min-w-0">
						<p class="text-sm font-medium text-gray-900 dark:text-white truncate">{rate.host}</p>
						<p class="text-xs text-gray-500 dark:text-gray-400">
							{formatPrice(rate.amount, rate.currency)}
							{#if rate.freeAbove != null}
								· free above {formatPrice(rate.freeAbove, rate.currency)}
							{/if}
						</p>
					</div>
					<div class="-my-1 flex shrink-0">
						<button
							type="button"
							onclick={() => openEdit(rate)}
							class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-brand hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
							aria-label="Edit shipping cost for {rate.host}"
							title="Edit"
						>
							<Pencil size={16} />
						</button>
						<button
							type="button"
							onclick={() => (deleting = rate)}
							class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-danger hover:bg-danger/10 rounded"
							aria-label="Delete shipping cost for {rate.host}"
							title="Delete"
						>
							<Trash2 size={16} />
						</button>
					</div>
				</li>
			{/each}
		</ul>
	{/if}
</section>

<ShippingRateModal
	isOpen={modalOpen}
	rate={editing}
	onClose={() => (modalOpen = false)}
	onSaved={handleSaved}
/>

<ConfirmModal
	isOpen={deleting !== null}
	title="Delete shipping cost"
	message="Remove the shipping cost for {deleting?.host ?? ''}? Its prices will be compared without shipping."
	onConfirm={handleConfirmDelete}
	onCancel={() => (deleting = null)}
	loading={deleteLoading}
/>
