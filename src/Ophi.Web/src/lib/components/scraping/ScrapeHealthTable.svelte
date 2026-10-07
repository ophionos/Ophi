<script lang="ts">
	import type { DomainHealth } from '$lib/api/client';
	import { formatTimeAgo } from '$lib/format';
	import { resolve } from '$app/paths';
	import { Activity, CircleCheckBig, CircleAlert, AlertTriangle } from 'lucide-svelte';
	import EmptyState from '$lib/components/shared/EmptyState.svelte';

	interface Props {
		domains: DomainHealth[];
	}

	let { domains }: Props = $props();

	function successRateBadge(rate: number): { text: string; class: string } {
		const pct = Math.round(rate * 100);
		if (rate >= 0.95) return { text: `${pct}%`, class: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400' };
		if (rate >= 0.80) return { text: `${pct}%`, class: 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400' };
		return { text: `${pct}%`, class: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400' };
	}

	function statusIconClass(rate: number): string {
		if (rate >= 0.95) return 'text-green-500';
		if (rate >= 0.80) return 'text-amber-500';
		return 'text-red-500';
	}

	function statusLevel(rate: number): 'healthy' | 'degraded' | 'unhealthy' {
		if (rate >= 0.95) return 'healthy';
		if (rate >= 0.80) return 'degraded';
		return 'unhealthy';
	}

	function formatDuration(ms: number): string {
		if (ms < 1000) return `${Math.round(ms)}ms`;
		return `${(ms / 1000).toFixed(1)}s`;
	}
</script>

{#if domains.length === 0}
	<EmptyState
		framed={false}
		icon={Activity}
		title="No scrape data yet"
		description="Scrapes appear here after your first product check."
	>
		<a
			href={resolve('/dashboard')}
			class="inline-flex items-center px-4 py-2 text-sm font-medium text-white bg-brand rounded-lg hover:bg-brand-hover"
		>
			Go to dashboard
		</a>
	</EmptyState>
{:else}
	<!-- Mobile cards -->
	<div class="md:hidden space-y-3">
		{#each domains as domain (domain.domain)}
			{@const badge = successRateBadge(domain.successRate)}
			{@const level = statusLevel(domain.successRate)}
			{@const iconCls = statusIconClass(domain.successRate)}
			<div class="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-4">
				<div class="flex items-center justify-between mb-3">
					<div class="flex items-center gap-2">
						{#if level === 'healthy'}
							<CircleCheckBig size={16} class={iconCls} />
						{:else if level === 'degraded'}
							<AlertTriangle size={16} class={iconCls} />
						{:else}
							<CircleAlert size={16} class={iconCls} />
						{/if}
						<span class="font-medium text-gray-900 dark:text-white text-sm truncate max-w-[200px]">{domain.domain}</span>
					</div>
					<span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium {badge.class}">
						{badge.text}
					</span>
				</div>
				<div class="grid grid-cols-2 gap-2 text-sm">
					<div>
						<span class="text-gray-500 dark:text-gray-400">Scrapes (7d)</span>
						<p class="font-medium text-gray-900 dark:text-white">{domain.totalScrapes}</p>
					</div>
					<div>
						<span class="text-gray-500 dark:text-gray-400">Today</span>
						<p class="font-medium text-gray-900 dark:text-white">{domain.scrapesToday}</p>
					</div>
					<div>
						<span class="text-gray-500 dark:text-gray-400">p50</span>
						<p class="font-medium text-gray-900 dark:text-white">{formatDuration(domain.p50DurationMs)}</p>
					</div>
					<div>
						<span class="text-gray-500 dark:text-gray-400">p95</span>
						<p class="font-medium text-gray-900 dark:text-white">{formatDuration(domain.p95DurationMs)}</p>
					</div>
				</div>
				{#if domain.lastFailureMessage}
					<div class="mt-3 pt-3 border-t border-gray-100 dark:border-gray-700">
						<p class="text-xs text-red-500 dark:text-red-400 truncate" title={domain.lastFailureMessage}>
							{domain.lastFailureMessage}
						</p>
						{#if domain.lastFailureAt}
							<p class="text-xs text-gray-400 mt-0.5">{formatTimeAgo(domain.lastFailureAt)}</p>
						{/if}
					</div>
				{/if}
			</div>
		{/each}
	</div>

	<!-- Desktop table -->
	<div class="hidden md:block overflow-x-auto">
		<table class="w-full" data-testid="scrape-health-table">
			<thead>
				<tr class="border-b border-gray-200 dark:border-gray-700">
					<th class="text-left py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Domain</th>
					<th class="text-center py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Success Rate</th>
					<th class="text-center py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Scrapes (7d)</th>
					<th class="text-center py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Today</th>
					<th class="text-center py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">p50</th>
					<th class="text-center py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">p95</th>
					<th class="text-left py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Last Error</th>
				</tr>
			</thead>
			<tbody class="divide-y divide-gray-100 dark:divide-gray-700/50">
				{#each domains as domain (domain.domain)}
					{@const badge = successRateBadge(domain.successRate)}
					{@const level = statusLevel(domain.successRate)}
					{@const iconCls = statusIconClass(domain.successRate)}
					<tr class="hover:bg-gray-50 dark:hover:bg-gray-800/50 transition-colors">
						<td class="py-3 px-4">
							<div class="flex items-center gap-2">
								{#if level === 'healthy'}
									<CircleCheckBig size={14} class={iconCls} />
								{:else if level === 'degraded'}
									<AlertTriangle size={14} class={iconCls} />
								{:else}
									<CircleAlert size={14} class={iconCls} />
								{/if}
								<span class="text-sm font-medium text-gray-900 dark:text-white">{domain.domain}</span>
							</div>
						</td>
						<td class="py-3 px-4 text-center">
							<span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium {badge.class}">
								{badge.text}
							</span>
						</td>
						<td class="py-3 px-4 text-center text-sm text-gray-700 dark:text-gray-300">{domain.totalScrapes}</td>
						<td class="py-3 px-4 text-center text-sm text-gray-700 dark:text-gray-300">{domain.scrapesToday}</td>
						<td class="py-3 px-4 text-center text-sm text-gray-700 dark:text-gray-300">{formatDuration(domain.p50DurationMs)}</td>
						<td class="py-3 px-4 text-center text-sm text-gray-700 dark:text-gray-300">{formatDuration(domain.p95DurationMs)}</td>
						<td class="py-3 px-4">
							{#if domain.lastFailureMessage}
								<p class="text-xs text-red-500 dark:text-red-400 truncate max-w-[250px]" title={domain.lastFailureMessage}>
									{domain.lastFailureMessage}
								</p>
								{#if domain.lastFailureAt}
									<p class="text-xs text-gray-400">{formatTimeAgo(domain.lastFailureAt)}</p>
								{/if}
							{:else}
								<span class="text-xs text-gray-400">--</span>
							{/if}
						</td>
					</tr>
				{/each}
			</tbody>
		</table>
	</div>
{/if}
