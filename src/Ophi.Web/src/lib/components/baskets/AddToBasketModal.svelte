<script lang="ts">
	import { LoaderCircle } from 'lucide-svelte';
	import Modal from '$lib/components/shared/Modal.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';
	import { api, type Basket, type BasketSummary } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';

	interface Props {
		open: boolean;
		productIds: string[];
		onClose: () => void;
		onAdded?: (basket: Basket) => void;
	}

	let { open, productIds, onClose, onAdded }: Props = $props();

	const NEW = 'new';

	let baskets = $state<BasketSummary[]>([]);
	let choice = $state<string>(NEW);
	let newName = $state('');
	let loading = $state(false);
	let saving = $state(false);
	let error = $state('');

	$effect(() => {
		if (!open) return;
		newName = '';
		error = '';
		void loadBaskets();
	});

	async function loadBaskets() {
		loading = true;
		try {
			baskets = (await api.listBaskets()).items;
			choice = baskets.length > 0 ? baskets[0].id : NEW;
		} catch (err) {
			baskets = [];
			choice = NEW;
			error = err instanceof Error ? err.message : 'Failed to load baskets';
		} finally {
			loading = false;
		}
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		error = '';

		if (choice === NEW && !newName.trim()) {
			error = 'Name is required';
			return;
		}

		saving = true;
		try {
			let target: { id: string; name: string };
			if (choice === NEW) {
				target = await api.createBasket(newName.trim());
			} else {
				target = baskets.find((b) => b.id === choice)!;
			}
			const updated = await api.addBasketItems(target.id, { productIds });
			toast.success(`Added to ${target.name}`);
			onAdded?.(updated);
			onClose();
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to add to basket';
		} finally {
			saving = false;
		}
	}
</script>

<Modal isOpen={open} title="Add to basket" size="md" {onClose}>
	<form onsubmit={handleSubmit} class="space-y-4 p-6">
		{#if error}
			<FormError variant="box" message={error} />
		{/if}

		<p class="text-sm text-gray-500 dark:text-gray-400">
			{productIds.length === 1 ? '1 product' : `${productIds.length} products`}
		</p>

		{#if loading}
			<LoaderCircle size={20} class="animate-spin text-gray-400" />
		{:else}
			<div class="space-y-1">
				{#each baskets as basket (basket.id)}
					<label class="flex min-h-11 items-center gap-3 text-gray-900 dark:text-white">
						<input type="radio" name="basket-choice" value={basket.id} bind:group={choice} />
						{basket.name}
					</label>
				{/each}
				<label class="flex min-h-11 items-center gap-3 text-gray-900 dark:text-white">
					<input type="radio" name="basket-choice" value={NEW} bind:group={choice} />
					New basket
				</label>
			</div>

			{#if choice === NEW}
				<div>
					<label
						for="new-basket-name"
						class="block text-sm font-medium text-gray-700 dark:text-gray-300">Basket name</label
					>
					<input
						id="new-basket-name"
						type="text"
						bind:value={newName}
						disabled={saving}
						class="mt-1 w-full px-3 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md bg-surface-field text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent"
					/>
				</div>
			{/if}
		{/if}

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
				disabled={saving || loading}
				class="flex items-center gap-2 px-4 min-h-11 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50"
			>
				{#if saving}
					<LoaderCircle size={16} class="animate-spin" />
				{/if}
				Add to basket
			</button>
		</div>
	</form>
</Modal>
