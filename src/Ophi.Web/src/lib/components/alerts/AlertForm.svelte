<script lang="ts">
	import { LoaderCircle, TriangleAlert } from 'lucide-svelte';
	import { ApiError, type AlertCondition } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface ExistingAlert {
		condition: AlertCondition;
		active: boolean;
	}

	interface Props {
		productName: string;
		currentPrice?: number;
		currency: string;
		existingAlerts?: ExistingAlert[];
		onSubmit: (targetPrice: number, condition: AlertCondition) => Promise<void>;
		onCancel: () => void;
	}

	let { productName, currentPrice, currency, existingAlerts = [], onSubmit, onCancel }: Props = $props();

	let targetPrice = $derived(currentPrice ? Math.floor(currentPrice * 0.9) : 0);
	let condition = $state<AlertCondition>('below');

	let loading = $state(false);
	let error = $state('');
	let fieldErrors = $state<Record<string, string[]>>({});

	const hasDuplicate = $derived(
		existingAlerts.some((a) => a.condition === condition && a.active)
	);

	function validateForm(): boolean {
		const errors: Record<string, string[]> = {};

		if (!targetPrice || targetPrice <= 0) {
			errors.targetprice = ['Target price must be greater than 0'];
		}

		if (!condition) {
			errors.condition = ['Please select a condition'];
		}

		fieldErrors = errors;
		return Object.keys(errors).length === 0;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!validateForm()) return;

		loading = true;
		error = '';
		fieldErrors = {};

		try {
			await onSubmit(targetPrice, condition);
		} catch (err) {
			if (err instanceof ApiError) {
				error = err.message;
				if (err.hasFieldErrors()) {
					fieldErrors = err.details;
				}
			} else {
				error = err instanceof Error ? err.message : 'An error occurred';
			}
		} finally {
			loading = false;
		}
	}

	function getFieldError(field: string): string | undefined {
		return fieldErrors[field.toLowerCase()]?.[0];
	}

	const conditions: { value: AlertCondition; label: string; description: string }[] = [
		{
			value: 'below',
			label: 'Price drops below',
			description: 'Get notified when the price falls below your target'
		},
		{
			value: 'above',
			label: 'Price goes above',
			description: 'Get notified when the price rises above your target'
		},
		{
			value: 'percentDrop',
			label: 'Price drops by percentage',
			description: 'Get notified when price drops by a percentage from reference'
		}
	];
</script>

<form onsubmit={handleSubmit} class="space-y-6">
	{#if error}
		<FormError variant="box" message={error} />
	{/if}

	{#if hasDuplicate}
		<div class="flex items-start gap-2 p-3 bg-warning/10 border border-warning/30 rounded-md">
			<TriangleAlert size={16} class="text-warning shrink-0 mt-0.5" />
			<p class="text-sm text-warning">
				You already have an active alert with this condition. Creating another may result in duplicate notifications.
			</p>
		</div>
	{/if}

	<div>
		<p class="text-sm text-gray-600 dark:text-gray-300 mb-4">
			Create a price alert for <span class="font-medium text-gray-900 dark:text-white"
				>{productName}</span
			>
		</p>
		{#if currentPrice}
			<p class="text-sm text-gray-500 dark:text-gray-400">
				Current price: <span class="font-medium">{formatPrice(currentPrice, currency)}</span>
			</p>
		{/if}
	</div>

	<div>
		<label for="condition" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
			Alert Condition <span class="text-danger">*</span>
		</label>
		<div class="space-y-2">
			{#each conditions as cond (cond.value)}
				<label
					class="flex items-start p-3 border rounded-md cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-700 {condition ===
					cond.value
						? 'border-brand bg-brand-light dark:bg-brand-dark/30'
						: 'border-gray-200 dark:border-gray-600'}"
				>
					<input
						type="radio"
						name="condition"
						value={cond.value}
						bind:group={condition}
						class="mt-0.5 h-4 w-4"
						disabled={loading}
					/>
					<div class="ml-3">
						<span class="block text-sm font-medium text-gray-900 dark:text-white">{cond.label}</span
						>
						<span class="block text-xs text-gray-500 dark:text-gray-400">{cond.description}</span>
					</div>
				</label>
			{/each}
		</div>
		{#if getFieldError('condition')}
			<p role="alert" class="mt-1 text-sm text-danger">{getFieldError('condition')}</p>
		{/if}
	</div>

	<div>
		<label for="targetPrice" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
			{condition === 'percentDrop' ? 'Drop Percentage (%)' : `Target Price (${currency})`}
			<span class="text-danger">*</span>
		</label>
		<div class="mt-1 relative rounded-md shadow-sm">
			<div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
				<span class="text-gray-500 dark:text-gray-400 sm:text-sm">
					{condition === 'percentDrop' ? '%' : currency}
				</span>
			</div>
			<input
				id="targetPrice"
				type="number"
				step={condition === 'percentDrop' ? '1' : '0.01'}
				min="0"
				bind:value={targetPrice}
				class="block w-full pl-12 pr-4 min-h-11 border border-gray-300 dark:border-gray-600 rounded-md focus:ring-2 focus:ring-brand focus:border-transparent bg-surface-field text-gray-900 dark:text-white {getFieldError(
					'targetprice'
				)
					? 'border-danger'
					: ''}"
				disabled={loading}
			/>
		</div>
		{#if condition === 'percentDrop'}
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				e.g., Enter 10 to be notified when price drops by 10% from reference
			</p>
		{:else}
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Enter the price threshold for your alert
			</p>
		{/if}
		{#if getFieldError('targetprice')}
			<p role="alert" class="mt-1 text-sm text-danger">{getFieldError('targetprice')}</p>
		{/if}
	</div>

	<div class="flex justify-end gap-3 pt-4 border-t border-gray-200 dark:border-gray-700">
		<button
			type="button"
			onclick={onCancel}
			disabled={loading}
			class="px-4 min-h-11 text-sm font-medium text-gray-700 dark:text-gray-300 bg-surface-field border border-gray-300 dark:border-gray-600 rounded-md hover:bg-surface-field-hover disabled:opacity-50"
		>
			Cancel
		</button>
		<button
			type="submit"
			disabled={loading}
			class="px-4 min-h-11 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 flex items-center gap-2"
		>
			{#if loading}
				<LoaderCircle size={16} class="animate-spin" />
			{/if}
			Create Alert
		</button>
	</div>
</form>
