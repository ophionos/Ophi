<script lang="ts">
	import type { PageData } from './$types';
	import { api } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';
	import { CHECK_INTERVAL_OPTIONS } from '$lib/format';

	let { data: pageData }: { data: PageData } = $props();

	let affiliatesEnabled = $state(true);
	let affiliatesLoading = $state(false);
	let defaultCheckInterval = $state('');
	let checkIntervalLoading = $state(false);
	let pageFetchDelay = $state(0);
	let pageFetchDelayLoading = $state(false);
	let scrapeCacheTtl = $state(0);
	let scrapeCacheTtlLoading = $state(false);
	let anomalyThreshold = $state(0);
	let anomalyThresholdLoading = $state(false);
	let autoPauseFailures = $state(0);
	let autoPauseFailuresLoading = $state(false);

	const SETTINGS_INTERVAL_OPTIONS = CHECK_INTERVAL_OPTIONS.map((opt, i) =>
		i === 0 ? { ...opt, label: 'System default (1 hour)' } : opt
	);

	$effect(() => {
		const settings = pageData.settings;
		if (settings) {
			affiliatesEnabled = settings.affiliatesEnabled;
			defaultCheckInterval = settings.defaultCheckIntervalMinutes?.toString() ?? '';
			pageFetchDelay = settings.pageFetchDelaySeconds ?? 0;
			scrapeCacheTtl = settings.scrapeCacheTtlMinutes ?? 0;
			anomalyThreshold = settings.anomalyThresholdPercent ?? 0;
			autoPauseFailures = settings.autoPauseAfterFailures ?? 0;
		}
	});

	async function handleToggleAffiliates() {
		affiliatesLoading = true;
		const newValue = !affiliatesEnabled;
		try {
			await api.updateSettings({ affiliatesEnabled: newValue });
			affiliatesEnabled = newValue;
			toast.success(newValue ? 'Affiliate links enabled' : 'Affiliate links disabled');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
		} finally {
			affiliatesLoading = false;
		}
	}

	async function handleCheckIntervalChange(e: Event) {
		const select = e.target as HTMLSelectElement;
		const newValue = select.value;
		checkIntervalLoading = true;
		try {
			const minutes = newValue === '' ? 0 : parseInt(newValue, 10);
			await api.updateSettings({ defaultCheckIntervalMinutes: minutes });
			defaultCheckInterval = newValue;
			toast.success('Default check interval updated');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
			select.value = defaultCheckInterval;
		} finally {
			checkIntervalLoading = false;
		}
	}

	async function handleFetchDelayChange(e: Event) {
		const input = e.target as HTMLInputElement;
		const newValue = parseInt(input.value, 10);
		if (isNaN(newValue) || newValue < 0 || newValue > 30) return;
		pageFetchDelayLoading = true;
		try {
			await api.updateSettings({ pageFetchDelaySeconds: newValue });
			pageFetchDelay = newValue;
			toast.success('Page fetch delay updated');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
			input.value = pageFetchDelay.toString();
		} finally {
			pageFetchDelayLoading = false;
		}
	}

	async function handleCacheTtlChange(e: Event) {
		const input = e.target as HTMLInputElement;
		const newValue = parseInt(input.value, 10);
		if (isNaN(newValue) || newValue < 0 || newValue > 1440) return;
		scrapeCacheTtlLoading = true;
		try {
			await api.updateSettings({ scrapeCacheTtlMinutes: newValue });
			scrapeCacheTtl = newValue;
			toast.success('Scrape cache TTL updated');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
			input.value = scrapeCacheTtl.toString();
		} finally {
			scrapeCacheTtlLoading = false;
		}
	}

	async function handleAnomalyThresholdChange(e: Event) {
		const input = e.target as HTMLInputElement;
		const newValue = parseInt(input.value, 10);
		if (isNaN(newValue) || newValue < 0 || newValue > 500) return;
		anomalyThresholdLoading = true;
		try {
			await api.updateSettings({ anomalyThresholdPercent: newValue });
			anomalyThreshold = newValue;
			toast.success(
				newValue === 0
					? 'Anomaly threshold reset to the system default'
					: `Anomaly threshold set to ${newValue}%`
			);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
			input.value = anomalyThreshold.toString();
		} finally {
			anomalyThresholdLoading = false;
		}
	}

	async function handleAutoPauseChange(e: Event) {
		const input = e.target as HTMLInputElement;
		const newValue = parseInt(input.value, 10);
		if (isNaN(newValue) || newValue < 0 || newValue > 50) return;
		autoPauseFailuresLoading = true;
		try {
			await api.updateSettings({ autoPauseAfterFailures: newValue });
			autoPauseFailures = newValue;
			toast.success(
				newValue === 0
					? 'Auto-pause reset to the system default'
					: `Auto-pause after ${newValue} failures`
			);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
			input.value = autoPauseFailures.toString();
		} finally {
			autoPauseFailuresLoading = false;
		}
	}
</script>

<svelte:head>
	<title>Scraping Settings - Ophi</title>
</svelte:head>

<div class="space-y-3">
	<!-- Affiliate toggle -->
	<div class="flex items-center justify-between bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Affiliate Links</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				When enabled, product links include your affiliate codes
			</p>
		</div>
		<button
			onclick={handleToggleAffiliates}
			disabled={affiliatesLoading}
			class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors {affiliatesEnabled ? 'bg-brand' : 'bg-gray-300 dark:bg-gray-600'} disabled:opacity-50"
			role="switch"
			aria-checked={affiliatesEnabled}
			data-testid="affiliates-toggle"
			aria-label="Toggle affiliate links"
		>
			<span
				class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {affiliatesEnabled ? 'translate-x-6' : 'translate-x-1'}"
			></span>
		</button>
	</div>

	<!-- Default check interval -->
	<div class="flex items-center justify-between bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Default Check Interval</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				How often new products are checked for price changes
			</p>
		</div>
		<select
			value={defaultCheckInterval}
			onchange={handleCheckIntervalChange}
			disabled={checkIntervalLoading}
			class="px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
			data-testid="default-check-interval"
		>
			{#each SETTINGS_INTERVAL_OPTIONS as option (option.value)}
				<option value={option.value}>{option.label}</option>
			{/each}
		</select>
	</div>

	<!-- Page fetch delay -->
	<div class="flex items-center justify-between bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Page Fetch Delay</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Seconds to wait before each scrape request (helps avoid rate limiting)
			</p>
		</div>
		<div class="flex items-center gap-2">
			<input
				type="number"
				min="0"
				max="30"
				value={pageFetchDelay}
				onchange={handleFetchDelayChange}
				disabled={pageFetchDelayLoading}
				class="w-20 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
				data-testid="page-fetch-delay"
			/>
			<span class="text-sm text-gray-500 dark:text-gray-400">sec</span>
		</div>
	</div>

	<!-- Scrape cache TTL -->
	<div class="flex items-center justify-between bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Scrape Cache TTL</p>
			<p class="text-xs text-gray-500 dark:text-gray-400" data-testid="scrape-cache-ttl-hint">
				Skip re-fetching pages scraped within this time window (0 = disabled)
			</p>
		</div>
		<div class="flex items-center gap-2">
			<input
				type="number"
				min="0"
				max="1440"
				value={scrapeCacheTtl}
				onchange={handleCacheTtlChange}
				disabled={scrapeCacheTtlLoading}
				class="w-20 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
				data-testid="scrape-cache-ttl"
			/>
			<span class="text-sm text-gray-500 dark:text-gray-400">min</span>
		</div>
	</div>

	<!-- Anomaly threshold -->
	<div class="flex items-center justify-between bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Anomaly Threshold</p>
			<p class="text-xs text-gray-500 dark:text-gray-400" data-testid="anomaly-threshold-hint">
				Flag products when price changes exceed this percentage. Set 0 to restore the system
				default — it does not turn anomaly flagging off.
			</p>
		</div>
		<div class="flex items-center gap-2">
			<input
				type="number"
				min="0"
				max="500"
				value={anomalyThreshold}
				onchange={handleAnomalyThresholdChange}
				disabled={anomalyThresholdLoading}
				class="w-20 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
				data-testid="anomaly-threshold"
			/>
			<span class="text-sm text-gray-500 dark:text-gray-400">%</span>
		</div>
	</div>

	<!-- Auto-pause after failures -->
	<div class="flex items-center justify-between bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700">
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Auto-Pause After Failures</p>
			<p class="text-xs text-gray-500 dark:text-gray-400" data-testid="auto-pause-failures-hint">
				Automatically pause a product URL after this many consecutive scrape failures. Set 0 to
				restore the system default — it does not turn auto-pause off.
			</p>
		</div>
		<div class="flex items-center gap-2">
			<input
				type="number"
				min="0"
				max="50"
				value={autoPauseFailures}
				onchange={handleAutoPauseChange}
				disabled={autoPauseFailuresLoading}
				class="w-20 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
				data-testid="auto-pause-failures"
			/>
			<span class="text-sm text-gray-500 dark:text-gray-400">fails</span>
		</div>
	</div>
</div>
