<script lang="ts">
	import { onMount } from 'svelte';
	import { WifiOff } from 'lucide-svelte';
	import { DATA_SOURCE_MESSAGE, type DataSourceMessage } from '$lib/offline/cache-policy';

	// The browser's own signal, plus the service worker's: it reports a saved copy also when the
	// network is up but the server is not.
	let browserOffline = $state(typeof navigator !== 'undefined' && navigator.onLine === false);
	let servedSaved = $state(false);
	let oldestSaved = $state<number | null>(null);

	const offline = $derived(browserOffline || servedSaved);
	const savedLabel = $derived(
		oldestSaved === null
			? null
			: new Date(oldestSaved).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
	);

	function backOnline() {
		browserOffline = false;
		servedSaved = false;
		oldestSaved = null;
	}

	function onWorkerMessage(event: MessageEvent) {
		const message = event.data as Partial<DataSourceMessage> | null;
		if (message?.type !== DATA_SOURCE_MESSAGE) return;

		if (!message.offline) {
			servedSaved = false;
			oldestSaved = null;
			return;
		}

		servedSaved = true;
		if (message.cachedAt !== undefined) {
			oldestSaved = oldestSaved === null ? message.cachedAt : Math.min(oldestSaved, message.cachedAt);
		}
	}

	onMount(() => {
		const goOffline = () => (browserOffline = true);
		window.addEventListener('offline', goOffline);
		window.addEventListener('online', backOnline);
		const worker = 'serviceWorker' in navigator ? navigator.serviceWorker : null;
		worker?.addEventListener('message', onWorkerMessage);

		return () => {
			window.removeEventListener('offline', goOffline);
			window.removeEventListener('online', backOnline);
			worker?.removeEventListener('message', onWorkerMessage);
		};
	});
</script>

{#if offline}
	<div
		data-testid="offline-banner"
		role="status"
		class="flex items-center gap-2 px-4 py-2 text-sm bg-amber-50 dark:bg-amber-900/20 border-b border-amber-200 dark:border-amber-800 text-amber-800 dark:text-amber-300"
	>
		<WifiOff size={16} class="shrink-0" />
		<span>
			You are offline.
			{#if savedLabel}Prices from {savedLabel}.{:else}Showing saved data.{/if}
			Changes need a connection.
		</span>
	</div>
{/if}
