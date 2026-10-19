<script lang="ts">
	import { ExternalLink, Trash2, LoaderCircle, TriangleAlert, RotateCcw, Settings, Trophy, Pause, Play, PackageX, ShieldCheck } from 'lucide-svelte';
	import type { ProductUrl } from '$lib/api/client';
	import { formatPrice, formatDate } from '$lib/format';
	import { onDestroy } from 'svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';

	interface Props {
		productUrl: ProductUrl;
		canDelete: boolean;
		isBestPrice?: boolean;
		isBuiltInStore?: boolean;
		onDelete?: (urlId: string) => Promise<void>;
		onRetry?: (urlId: string) => Promise<void>;
		onResume?: (urlId: string) => Promise<void>;
		/** Set only when the server offers challenge solving (GET /challenge/available). */
		onSolveChallenge?: (urlId: string) => void;
	}

	let {
		productUrl,
		canDelete,
		isBestPrice = false,
		isBuiltInStore = false,
		onDelete,
		onRetry,
		onResume,
		onSolveChallenge
	}: Props = $props();

	let deleting = $state(false);
	let confirmingDelete = $state(false);
	let retrying = $state(false);
	let resuming = $state(false);
	let cooldownRemaining = $state(0);
	let cooldownTimer: ReturnType<typeof setInterval> | null = null;

	// Why tracking stopped, as visible text. These used to live only in `title` attributes, which
	// don't exist on touch — leaving a red "Paused" pill and no way to find out what happened.
	// The anti-bot auto-pause (CheckProductPriceHandler) pauses WITHOUT setting SuspiciousReason,
	// so lastError is the only explanation that path produces.
	const failureExplanation = $derived.by(() => {
		if (productUrl.status === 'paused') {
			return (
				productUrl.suspiciousReason ?? productUrl.lastError ?? 'Paused after repeated failed checks.'
			);
		}
		if (productUrl.status === 'suspicious') {
			return productUrl.suspiciousReason ?? productUrl.lastError ?? null;
		}
		return productUrl.lastError ?? null;
	});

	// A store refusing this network is not a user error and retrying cannot fix it, so say so
	// instead of letting it read as "you did something suspicious".
	const isBlockedFromNetwork = $derived(
		productUrl.status === 'paused' &&
			/anti-bot|blocked/i.test(productUrl.lastError ?? productUrl.suspiciousReason ?? '')
	);

	// Same rule as the server's StartChallenge.IsChallengeError.
	const canSolveChallenge = $derived(
		onSolveChallenge !== undefined && /anti-bot/i.test(productUrl.lastError ?? '')
	);

	function getDomain(url: string): string {
		try {
			return new URL(url).hostname;
		} catch {
			return url;
		}
	}

	// The button sits beside retry and open, so a mis-tap must not stop tracking the store.
	async function handleConfirmDelete() {
		deleting = true;
		try {
			await onDelete?.(productUrl.id);
		} finally {
			deleting = false;
			confirmingDelete = false;
		}
	}

	async function handleRetry() {
		if (retrying || cooldownRemaining > 0) return;
		retrying = true;
		try {
			await onRetry?.(productUrl.id);
			startCooldown();
		} finally {
			retrying = false;
		}
	}

	async function handleResume() {
		if (resuming) return;
		resuming = true;
		try {
			await onResume?.(productUrl.id);
		} finally {
			resuming = false;
		}
	}

	function startCooldown() {
		cooldownRemaining = 60;
		cooldownTimer = setInterval(() => {
			cooldownRemaining--;
			if (cooldownRemaining <= 0) {
				cooldownRemaining = 0;
				if (cooldownTimer) {
					clearInterval(cooldownTimer);
					cooldownTimer = null;
				}
			}
		}, 1000);
	}

	onDestroy(() => {
		if (cooldownTimer) {
			clearInterval(cooldownTimer);
		}
	});
</script>

<div class="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-700 rounded-lg">
	<div class="flex items-center gap-3 min-w-0 flex-1">
		<div class="min-w-0 flex-1">
			<p class="text-sm font-medium text-gray-900 dark:text-white truncate">
				{getDomain(productUrl.url)}
			</p>
			<div class="flex items-center gap-2 mt-0.5">
				{#if productUrl.currentPrice}
					<span class="text-sm text-gray-600 dark:text-gray-300">
						{formatPrice(productUrl.currentPrice, productUrl.currency)}
					</span>
					{#if isBestPrice}
						<span class="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[10px] font-semibold rounded-full bg-price-drop-subtle text-price-drop" title="Lowest price across all stores">
							<Trophy size={9} />
							Best
						</span>
					{/if}
				{:else}
					<span class="text-xs text-gray-400">No price yet</span>
				{/if}
				{#if productUrl.status === 'suspicious'}
					<span
						class="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[10px] font-semibold rounded-full bg-warning/15 text-warning"
						title={productUrl.suspiciousReason ?? 'Suspicious scrape detected'}
						data-testid="suspicious-badge"
					>
						<TriangleAlert size={9} />
						Suspicious
					</span>
				{/if}
				{#if productUrl.status === 'paused'}
					<span
						class="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[10px] font-semibold rounded-full bg-danger/10 text-danger"
						title={productUrl.suspiciousReason ?? productUrl.lastError ?? 'Tracking paused'}
						data-testid="paused-badge"
					>
						<Pause size={9} />
						Paused
					</span>
				{/if}
				{#if productUrl.isOutOfStock}
					<span
						class="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[10px] font-semibold rounded-full bg-danger/10 text-danger"
						data-testid="out-of-stock-badge"
					>
						<PackageX size={9} />
						Out of Stock
					</span>
				{/if}
				{#if productUrl.lastCheckedAt}
					<span class="text-xs text-gray-400">
						· {formatDate(productUrl.lastCheckedAt)}
					</span>
				{/if}
				{#if productUrl.lastError && productUrl.status !== 'paused'}
					<span class="text-xs text-danger flex items-center gap-0.5" title={productUrl.lastError}>
						<TriangleAlert size={12} />
						Error
					</span>
				{/if}
			</div>
			{#if failureExplanation}
				<p
					class="mt-1 text-xs text-danger break-words"
					data-testid="url-failure-reason"
				>
					{failureExplanation}
				</p>
				{#if isBlockedFromNetwork}
					<p
						class="mt-0.5 text-xs text-gray-500 dark:text-gray-400 break-words"
						data-testid="url-blocked-hint"
					>
						{#if canSolveChallenge}
							This store is refusing automated requests. Solve the challenge once to let checks
							through for 7 days.
						{:else}
							This store is refusing automated requests from this server's network. Retrying won't
							change that — resume the URL if the network changes.
						{/if}
					</p>
				{/if}
			{/if}
		</div>
	</div>
	<div class="-my-2 -mr-2 flex ml-2 shrink-0">
		{#if productUrl.status === 'paused' && onResume}
			<button
				onclick={handleResume}
				disabled={resuming}
				class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded disabled:opacity-50 disabled:cursor-not-allowed"
				title="Resume monitoring"
				aria-label="Resume monitoring"
				data-testid="resume-url-button"
			>
				{#if resuming}
					<LoaderCircle size={16} class="animate-spin" />
				{:else}
					<Play size={16} />
				{/if}
			</button>
		{/if}
		{#if canSolveChallenge}
			<button
				onclick={() => onSolveChallenge?.(productUrl.id)}
				class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/30 rounded"
				title="Solve challenge"
				aria-label="Solve challenge"
				data-testid="solve-challenge-button"
			>
				<ShieldCheck size={16} />
			</button>
		{/if}
		{#if onRetry}
			<button
				onclick={handleRetry}
				disabled={retrying || cooldownRemaining > 0}
				class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-orange-600 dark:hover:text-orange-400 hover:bg-orange-50 dark:hover:bg-orange-900/30 rounded disabled:opacity-50 disabled:cursor-not-allowed"
				title={cooldownRemaining > 0 ? `Retry available in ${cooldownRemaining}s` : 'Retry scraping'}
				aria-label={cooldownRemaining > 0 ? `Retry available in ${cooldownRemaining}s` : 'Retry scraping'}
				data-testid="retry-scrape-button"
			>
				{#if retrying}
					<LoaderCircle size={16} class="animate-spin" />
				{:else if cooldownRemaining > 0}
					<span class="text-xs font-medium w-4 text-center">{cooldownRemaining}</span>
				{:else}
					<RotateCcw size={16} />
				{/if}
			</button>
		{/if}
		<a
			href={productUrl.affiliateUrl ?? productUrl.url}
			target="_blank"
			rel="noopener noreferrer external"
			class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-brand dark:hover:text-brand-muted hover:bg-brand-light dark:hover:bg-brand-dark/30 rounded"
			title="Open in new tab"
			aria-label="Open in new tab"
		>
			<ExternalLink size={16} />
		</a>
		{#if !isBuiltInStore}
			<a
				href={`/stores?edit=${encodeURIComponent(getDomain(productUrl.url))}`}
				class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-brand-secondary dark:hover:text-brand-secondary-dark hover:bg-brand-secondary/10 rounded"
				title="Store configuration"
				aria-label="Store configuration"
			>
				<Settings size={16} />
			</a>
		{/if}
		{#if canDelete}
			<button
				onclick={() => (confirmingDelete = true)}
				disabled={deleting}
				class="flex min-h-11 min-w-11 items-center justify-center text-gray-400 hover:text-danger hover:bg-danger/10 rounded disabled:opacity-50"
				title="Remove URL"
				aria-label="Remove URL"
			>
				{#if deleting}
					<LoaderCircle size={16} class="animate-spin" />
				{:else}
					<Trash2 size={16} />
				{/if}
			</button>
		{/if}
	</div>
</div>

<ConfirmModal
	isOpen={confirmingDelete}
	title="Remove URL"
	message="Stop tracking {getDomain(productUrl.url)} for this product? Its past prices stay in the price history."
	confirmText="Remove"
	loading={deleting}
	onConfirm={handleConfirmDelete}
	onCancel={() => (confirmingDelete = false)}
/>
