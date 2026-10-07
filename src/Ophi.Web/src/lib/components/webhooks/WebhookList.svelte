<script lang="ts">
	import { Plus, Webhook, Send, Pencil, Trash2 } from 'lucide-svelte';
	import type { WebhookTarget } from '$lib/api/client';

	interface Props {
		webhooks: WebhookTarget[];
		testingId?: string;
		deletingId?: string;
		onAdd: () => void;
		onEdit: (webhook: WebhookTarget) => void;
		onTest: (id: string) => void;
		onDelete: (id: string) => void;
	}

	let { webhooks, testingId, deletingId, onAdd, onEdit, onTest, onDelete }: Props = $props();
</script>

<div class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3">
	<div class="flex items-center justify-between">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Outbound Webhooks</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Fire a JSON payload to any HTTP endpoint when price events occur
			</p>
		</div>
		<button
			onclick={onAdd}
			class="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md transition-colors"
		>
			<Plus size={14} />
			Add
		</button>
	</div>

	{#if webhooks.length === 0}
		<div class="flex items-center gap-2 py-2 text-xs text-gray-400 dark:text-gray-500">
			<Webhook size={14} />
			No webhooks configured. Add one to forward events to ntfy, Gotify, Home Assistant, or any HTTP endpoint.
		</div>
	{:else}
		<ul class="space-y-2">
			{#each webhooks as webhook (webhook.id)}
				<li class="flex items-center gap-3 py-2 border-t border-gray-100 dark:border-gray-700/50 first:border-t-0">
					<div class="flex-1 min-w-0">
						<div class="flex items-center gap-2">
							<span class="text-sm font-medium text-gray-900 dark:text-white truncate">{webhook.name}</span>
							{#if !webhook.isEnabled}
								<span class="text-xs px-1.5 py-0.5 bg-gray-100 dark:bg-gray-700 text-gray-500 dark:text-gray-400 rounded">disabled</span>
							{/if}
						</div>
						<p class="text-xs text-gray-400 dark:text-gray-500 truncate">{webhook.url}</p>
						<div class="flex flex-wrap gap-1 mt-1">
							{#each webhook.events as ev (ev)}
								<span class="text-xs px-1.5 py-0.5 bg-brand-subtle dark:bg-brand-dark/30 text-brand dark:text-brand-muted rounded">{ev}</span>
							{/each}
						</div>
					</div>
					<div class="flex items-center gap-1 shrink-0">
						<button
							onclick={() => onTest(webhook.id)}
							disabled={testingId === webhook.id}
							title="Send test payload"
							class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted rounded transition-colors disabled:opacity-50"
						>
							<Send size={14} />
						</button>
						<button
							onclick={() => onEdit(webhook)}
							title="Edit webhook"
							class="p-1.5 text-gray-400 hover:text-brand dark:hover:text-brand-muted rounded transition-colors"
						>
							<Pencil size={14} />
						</button>
						<button
							onclick={() => onDelete(webhook.id)}
							disabled={deletingId === webhook.id}
							title="Delete webhook"
							class="p-1.5 text-gray-400 hover:text-red-500 dark:hover:text-red-400 rounded transition-colors disabled:opacity-50"
						>
							<Trash2 size={14} />
						</button>
					</div>
				</li>
			{/each}
		</ul>
	{/if}

	<p class="text-xs text-gray-400 dark:text-gray-500">
		Payload includes: event type, product name, old &amp; new price, currency, and timestamp.
		Compatible with ntfy, Gotify, Home Assistant webhooks, n8n, and any HTTP endpoint.
	</p>
</div>
