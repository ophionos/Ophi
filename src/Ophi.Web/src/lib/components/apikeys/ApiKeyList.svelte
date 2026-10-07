<script lang="ts">
	import { Plus, Key, Trash2 } from 'lucide-svelte';
	import type { ApiKeyInfo } from '$lib/api/client';

	interface Props {
		keys: ApiKeyInfo[];
		deletingId?: string;
		onCreate: () => void;
		onDelete: (id: string) => void;
	}

	let { keys, deletingId, onCreate, onDelete }: Props = $props();
</script>

<div class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3">
	<div class="flex items-center justify-between">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">API Keys</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Authenticate scripts, cron jobs, and integrations with bearer tokens
			</p>
		</div>
		<button
			onclick={onCreate}
			class="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md transition-colors"
		>
			<Plus size={14} />
			Create
		</button>
	</div>

	{#if keys.length === 0}
		<div class="flex items-center gap-2 py-2 text-xs text-gray-400 dark:text-gray-500">
			<Key size={14} />
			No API keys created. Create one to use Ophi from scripts and automations.
		</div>
	{:else}
		<ul class="space-y-2">
			{#each keys as key (key.id)}
				<li class="flex items-center gap-3 py-2 border-t border-gray-100 dark:border-gray-700/50 first:border-t-0">
					<div class="flex-1 min-w-0">
						<div class="flex items-center gap-2">
							<span class="text-sm font-medium text-gray-900 dark:text-white truncate">{key.name}</span>
							{#if key.expiresAt && new Date(key.expiresAt) < new Date()}
								<span class="text-xs px-1.5 py-0.5 bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400 rounded">expired</span>
							{/if}
						</div>
						<div class="flex flex-wrap gap-1 mt-1">
							{#each key.scopes as scope (scope)}
								<span class="text-xs px-1.5 py-0.5 bg-brand-subtle dark:bg-brand-dark/30 text-brand dark:text-brand-muted rounded">{scope}</span>
							{/each}
						</div>
						<p class="text-xs text-gray-400 dark:text-gray-500 mt-0.5">
							Created {new Date(key.createdAt).toLocaleDateString()}
							{#if key.lastUsedAt}
								&middot; Last used {new Date(key.lastUsedAt).toLocaleDateString()}
							{:else}
								&middot; Never used
							{/if}
						</p>
					</div>
					<button
						onclick={() => onDelete(key.id)}
						disabled={deletingId === key.id}
						title="Revoke API key"
						class="p-1.5 text-gray-400 hover:text-red-500 dark:hover:text-red-400 rounded transition-colors disabled:opacity-50 shrink-0"
					>
						<Trash2 size={14} />
					</button>
				</li>
			{/each}
		</ul>
	{/if}

	<p class="text-xs text-gray-400 dark:text-gray-500">
		Use <code class="bg-gray-100 dark:bg-gray-700 px-1 rounded">Authorization: Bearer &lt;key&gt;</code> to authenticate API requests from scripts.
	</p>
</div>
