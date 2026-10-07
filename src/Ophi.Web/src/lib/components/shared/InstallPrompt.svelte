<script lang="ts">
	import { onMount } from 'svelte';
	import { Download, X } from 'lucide-svelte';

	// Dismissal is persisted: the browser re-fires `beforeinstallprompt` on later visits, so without
	// this the banner came back every page load no matter how often it was dismissed.
	const DISMISSED_KEY = 'ophi:install-prompt-dismissed';

	let deferredPrompt = $state<any>(null);
	let dismissed = $state(false);

	const show = $derived(!!deferredPrompt && !dismissed);

	onMount(() => {
		try {
			dismissed = localStorage.getItem(DISMISSED_KEY) === 'true';
		} catch {
			// Storage unavailable (private mode / blocked cookies) — fall back to per-session dismissal.
		}

		function handleBeforeInstallPrompt(e: Event) {
			e.preventDefault();
			deferredPrompt = e;
		}

		window.addEventListener('beforeinstallprompt', handleBeforeInstallPrompt);

		return () => {
			window.removeEventListener('beforeinstallprompt', handleBeforeInstallPrompt);
		};
	});

	async function handleInstall() {
		if (!deferredPrompt) return;
		deferredPrompt.prompt();
		const result = await deferredPrompt.userChoice;
		if (result.outcome === 'accepted') {
			deferredPrompt = null;
		}
	}

	function handleDismiss() {
		dismissed = true;
		deferredPrompt = null;
		try {
			localStorage.setItem(DISMISSED_KEY, 'true');
		} catch {
			// Storage unavailable — the dismissal still holds for this session.
		}
	}
</script>

{#if show}
	<div
		class="fixed bottom-0 inset-x-0 z-50 px-4 py-3 bg-brand text-white shadow-lg flex items-center justify-between gap-3"
		role="banner"
		aria-label="Install app prompt"
		data-testid="install-prompt"
	>
		<div class="flex items-center gap-2 text-sm">
			<Download size={16} />
			<span>Install Ophi for a faster, offline-capable experience.</span>
		</div>
		<div class="flex items-center gap-2 shrink-0">
			<button
				onclick={handleInstall}
				class="px-3 py-1 text-xs font-medium bg-white text-brand rounded hover:bg-gray-100"
				data-testid="install-prompt-install"
			>
				Install
			</button>
			<button
				onclick={handleDismiss}
				class="p-1 text-white/80 hover:text-white rounded"
				aria-label="Dismiss install prompt"
				data-testid="install-prompt-dismiss"
			>
				<X size={16} />
			</button>
		</div>
	</div>
{/if}
