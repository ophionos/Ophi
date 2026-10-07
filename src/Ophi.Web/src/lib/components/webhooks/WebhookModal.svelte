<script lang="ts">
	import Modal from '$lib/components/shared/Modal.svelte';
	import type {
		WebhookTarget,
		CreateWebhookTargetRequest,
		UpdateWebhookTargetRequest,
		WebhookEvent
	} from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		isOpen: boolean;
		target?: WebhookTarget;
		onClose: () => void;
		onSave: (data: CreateWebhookTargetRequest | UpdateWebhookTargetRequest) => Promise<void>;
	}

	let { isOpen, target, onClose, onSave }: Props = $props();

	const isEdit = $derived(!!target);
	const title = $derived(isEdit ? 'Edit Webhook' : 'Add Webhook');

	let name = $state('');
	let url = $state('');
	let selectedEvents = $state<WebhookEvent[]>([]);
	let isEnabled = $state(true);
	let saving = $state(false);
	let error = $state('');

	const ALL_EVENTS: { value: WebhookEvent; label: string; description: string }[] = [
		{ value: 'alert_fired', label: 'Alert fired', description: 'When a price alert condition is met' },
		{ value: 'price_changed', label: 'Price changed', description: 'When a product price changes' },
		{ value: 'scrape_failed', label: 'Scrape failed', description: 'When a product fails to scrape' }
	];

	$effect(() => {
		if (isOpen) {
			name = target?.name ?? '';
			url = target?.url ?? '';
			selectedEvents = target?.events ? [...target.events] : ['alert_fired'];
			isEnabled = target?.isEnabled ?? true;
			error = '';
		}
	});

	function toggleEvent(event: WebhookEvent) {
		if (selectedEvents.includes(event)) {
			selectedEvents = selectedEvents.filter((e) => e !== event);
		} else {
			selectedEvents = [...selectedEvents, event];
		}
	}

	async function handleSubmit(e: SubmitEvent) {
		e.preventDefault();
		if (selectedEvents.length === 0) {
			error = 'Select at least one event type.';
			return;
		}
		saving = true;
		error = '';
		try {
			await onSave({ name: name.trim(), url: url.trim(), events: selectedEvents, isEnabled });
		} catch (err) {
			error = err instanceof Error ? err.message : 'Failed to save webhook';
		} finally {
			saving = false;
		}
	}
</script>

<Modal {isOpen} {title} size="md" position="center" {onClose}>
	<form onsubmit={handleSubmit} class="p-4 space-y-4">
		<!-- Name -->
		<div>
			<label for="webhook-name" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
				Name
			</label>
			<input
				id="webhook-name"
				type="text"
				bind:value={name}
				placeholder="My Home Assistant webhook"
				required
				maxlength="100"
				class="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-brand"
			/>
		</div>

		<!-- URL -->
		<div>
			<label for="webhook-url" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
				URL
			</label>
			<input
				id="webhook-url"
				type="url"
				bind:value={url}
				placeholder="https://..."
				required
				class="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-brand"
			/>
		</div>

		<!-- Events -->
		<div>
			<p class="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Fire on</p>
			<div class="space-y-2">
				{#each ALL_EVENTS as ev (ev.value)}
					<label class="flex items-start gap-3 cursor-pointer">
						<input
							type="checkbox"
							checked={selectedEvents.includes(ev.value)}
							onchange={() => toggleEvent(ev.value)}
							class="mt-0.5 h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-brand focus:ring-brand"
						/>
						<span class="flex-1">
							<span class="text-sm font-medium text-gray-800 dark:text-gray-200">{ev.label}</span>
							<span class="block text-xs text-gray-500 dark:text-gray-400">{ev.description}</span>
						</span>
					</label>
				{/each}
			</div>
		</div>

		<!-- Enabled toggle -->
		<div class="flex items-center justify-between">
			<span class="text-sm font-medium text-gray-700 dark:text-gray-300">Enabled</span>
			<button
				type="button"
				onclick={() => (isEnabled = !isEnabled)}
				class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors {isEnabled ? 'bg-brand' : 'bg-gray-300 dark:bg-gray-600'}"
				role="switch"
				aria-checked={isEnabled}
				aria-label="Toggle enabled"
			>
				<span
					class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {isEnabled ? 'translate-x-6' : 'translate-x-1'}"
				></span>
			</button>
		</div>

		{#if error}
			<FormError message={error} />
		{/if}

		<!-- Actions -->
		<div class="flex gap-2 pt-2">
			<button
				type="button"
				onclick={onClose}
				class="flex-1 px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-lg transition-colors"
			>
				Cancel
			</button>
			<button
				type="submit"
				disabled={saving}
				class="flex-1 px-4 py-2 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-lg disabled:opacity-50 transition-colors"
			>
				{saving ? 'Saving...' : isEdit ? 'Save Changes' : 'Add Webhook'}
			</button>
		</div>
	</form>
</Modal>
