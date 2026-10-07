<script lang="ts">
	import { api, type ScrapeLogEntry } from '$lib/api/client';
	import { formatPrice } from '$lib/format';
	import { CircleCheckBig, CircleX, LoaderCircle, Activity } from 'lucide-svelte';
	import SectionCard from '$lib/components/shared/SectionCard.svelte';

	interface Props {
		productId: string;
		currency?: string;
	}

	let { productId, currency = 'USD' }: Props = $props();

	let loading = $state(false);
	let entries = $state<ScrapeLogEntry[]>([]);
	let loaded = $state(false);

	async function handleToggle(open: boolean) {
		if (open && !loaded) {
			loading = true;
			try {
				const result = await api.getScrapeLog(productId, 20);
				entries = result.items;
				loaded = true;
			} catch (err) {
				console.error('Failed to load scrape history:', err);
			} finally {
				loading = false;
			}
		}
	}

	function formatDuration(ms: number): string {
		if (ms < 1000) return `${ms}ms`;
		return `${(ms / 1000).toFixed(1)}s`;
	}

	function getDomain(url: string): string {
		try {
			return new URL(url).hostname;
		} catch {
			return url;
		}
	}
</script>

<SectionCard
	title="Scrape History"
	icon={Activity}
	collapsible
	open={false}
	onToggle={handleToggle}
	bodyClass=""
>
	{#if loading}
		<div class="flex justify-center py-8" data-testid="scrape-loading">
			<LoaderCircle size={24} class="animate-spin text-gray-400" />
		</div>
	{:else if entries.length === 0}
		<div class="p-4 text-center text-gray-500 dark:text-gray-400 text-sm">
			No scrape history available
		</div>
	{:else}
		<div class="divide-y divide-gray-100 dark:divide-gray-700">
			{#each entries as entry (entry.id)}
				<div class="px-4 py-3 flex items-center gap-3">
					{#if entry.success}
						<CircleCheckBig size={16} class="text-green-500 shrink-0" />
					{:else}
						<CircleX size={16} class="text-red-500 shrink-0" />
					{/if}
					<div class="flex-1 min-w-0">
						<div class="flex items-center gap-2">
							{#if entry.price}
								<span class="text-sm font-medium text-gray-900 dark:text-white">
									{formatPrice(entry.price, currency)}
								</span>
							{/if}
							{#if entry.url}
								<span class="text-xs text-gray-400 truncate">
									{getDomain(entry.url)}
								</span>
							{/if}
						</div>
						{#if entry.error}
							<p class="text-xs text-red-500 dark:text-red-400 truncate" title={entry.error}>
								{entry.error}
							</p>
						{/if}
					</div>
					<div class="text-right shrink-0">
						<span class="text-xs text-gray-400">{formatDuration(entry.durationMs)}</span>
						<p class="text-xs text-gray-400">
							{new Date(entry.createdAt).toLocaleString()}
						</p>
					</div>
				</div>
			{/each}
		</div>
	{/if}
</SectionCard>
