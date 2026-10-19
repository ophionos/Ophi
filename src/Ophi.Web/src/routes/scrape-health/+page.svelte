<script lang="ts">
	import type { PageData } from './$types';
	import { api, type ScrapeHealthResponse } from '$lib/api/client';
	import ScrapeHealthTable from '$lib/components/scraping/ScrapeHealthTable.svelte';
	import SectionCard from '$lib/components/shared/SectionCard.svelte';
	import StatCard from '$lib/components/shared/StatCard.svelte';
	import PageHeader from '$lib/components/shared/PageHeader.svelte';
	import { Activity, RefreshCw, CircleCheckBig, AlertTriangle, CircleAlert } from 'lucide-svelte';

	let { data: pageData }: { data: PageData } = $props();

	let health = $derived<ScrapeHealthResponse | null>(pageData.health);
	let loadError = $state('');
	let refreshing = $state(false);

	async function handleRefresh() {
		refreshing = true;
		loadError = '';
		try {
			health = await api.getScrapeHealth();
		} catch (err) {
			loadError = err instanceof Error ? err.message : 'Failed to load scrape health';
		} finally {
			refreshing = false;
		}
	}
</script>

<svelte:head>
	<title>Scrape Health - Ophi</title>
</svelte:head>

<div class="space-y-6">
	<PageHeader title="Scrape Health" description="How reliably each store domain scraped over the last 7 days">
		{#snippet actions()}
			<button
				onclick={handleRefresh}
				disabled={refreshing}
				class="flex items-center gap-2 px-3 min-h-11 text-sm font-medium text-gray-700 dark:text-gray-300 bg-surface-1 border border-gray-200 dark:border-gray-700 rounded-lg hover:bg-surface-2 transition-colors disabled:opacity-50"
			>
				<RefreshCw size={14} class={refreshing ? 'animate-spin' : ''} />
				Refresh
			</button>
		{/snippet}
	</PageHeader>

	{#if loadError}
		<div
			class="bg-danger/10 border border-danger/30 rounded-lg p-4"
		>
			<p role="alert" class="text-danger">{loadError}</p>
			<button
				onclick={handleRefresh}
				class="mt-2 text-sm font-medium text-danger hover:text-danger/80"
			>
				Try again
			</button>
		</div>
	{:else if health}
		{@const summary = health.summary}
		<!-- Summary cards -->
		<div class="grid grid-cols-2 md:grid-cols-4 gap-4">
			<StatCard label="Domains" value={summary.totalDomains} />
			<StatCard label="Scrapes (7d)" value={summary.totalScrapes7d} />
			<!-- A null rate means no scrapes landed in the window: there is nothing to score, so say
			     so rather than rendering a confident green 100%. A rate of 0 is real data. -->
			<StatCard
				label="Success Rate"
				testid="success-rate-card"
				value={summary.overallSuccessRate === null
					? 'No data'
					: `${Math.round(summary.overallSuccessRate * 100)}%`}
				valueClass={summary.overallSuccessRate === null
					? 'text-gray-400 dark:text-gray-500'
					: summary.overallSuccessRate >= 0.95
						? 'text-success'
						: summary.overallSuccessRate >= 0.8
							? 'text-warning'
							: 'text-danger'}
			/>
			<StatCard label="Status">
				<div class="flex items-center gap-3">
					{#if summary.domainsHealthy > 0}
						<span class="flex items-center gap-1 text-xs text-success">
							<CircleCheckBig size={12} />
							{summary.domainsHealthy}
						</span>
					{/if}
					{#if summary.domainsDegraded > 0}
						<span class="flex items-center gap-1 text-xs text-warning">
							<AlertTriangle size={12} />
							{summary.domainsDegraded}
						</span>
					{/if}
					{#if summary.domainsUnhealthy > 0}
						<span class="flex items-center gap-1 text-xs text-danger">
							<CircleAlert size={12} />
							{summary.domainsUnhealthy}
						</span>
					{/if}
					{#if summary.totalDomains === 0}
						<span class="text-xl font-bold text-gray-400 dark:text-gray-500">No data</span>
					{/if}
				</div>
			</StatCard>
		</div>

		<!-- Domain health table -->
		<SectionCard
			title="Per-Domain Health"
			subtitle="Last 7 days of scrape activity per store domain"
			icon={Activity}
		>
			<ScrapeHealthTable domains={health.domains} />
		</SectionCard>
	{/if}
</div>
