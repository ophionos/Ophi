<script lang="ts">
	import {
		ChevronDown,
		CircleAlert,
		SearchX,
		ShieldAlert,
		TriangleAlert,
		LoaderCircle
	} from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import type { AttentionKind, Product } from '$lib/api/client';
	import { attentionCause, fixAction, hostOf } from '$lib/utils/watchlist';

	interface Props {
		/** Products that need the user, worst first; may be a first slice of `count`. */
		products: Product[];
		/** Account-wide, so it can exceed the products shown. */
		count: number;
		challengeAvailable: boolean;
		onRetry: (product: Product) => Promise<void>;
		onSolveCheck: (product: Product) => void;
		onShowInTable: () => void;
	}

	let { products, count, challengeAvailable, onRetry, onSolveCheck, onShowInTable }: Props =
		$props();

	const STORAGE_KEY = 'watchlist-action-card-collapsed';

	function readCollapsed(): boolean {
		try {
			return localStorage.getItem(STORAGE_KEY) === 'true';
		} catch {
			return false;
		}
	}

	let collapsed = $state(readCollapsed());
	let busyId = $state<string | null>(null);

	function toggle() {
		collapsed = !collapsed;
		try {
			localStorage.setItem(STORAGE_KEY, String(collapsed));
		} catch {
			// A per-viewer convenience: the card still works without storage.
		}
	}

	async function retry(product: Product) {
		busyId = product.id;
		try {
			await onRetry(product);
		} finally {
			busyId = null;
		}
	}

	const ICONS: Record<AttentionKind, typeof CircleAlert> = {
		blocked: ShieldAlert,
		priceNotFound: SearchX,
		failing: CircleAlert,
		priceAnomaly: TriangleAlert,
		outOfStock: CircleAlert,
		paused: CircleAlert
	};

	const lines = $derived(products.filter((p) => p.attention?.isAction));
	const hidden = $derived(Math.max(0, count - lines.length));
	const outlineButton =
		'inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-gray-300 px-3 text-sm font-medium text-gray-700 hover:bg-surface-2 disabled:opacity-50 dark:border-gray-600 dark:text-gray-200';
</script>

{#if count > 0}
	<!-- Quiet on purpose: a normal card with a small amber marker, outline buttons, no tinted rows. -->
	<section
		class="mb-4 rounded-lg border border-gray-200 bg-surface-1 dark:border-gray-700"
		aria-labelledby="needs-action-title"
		data-testid="needs-action-card"
	>
		<div class="flex flex-wrap items-center gap-2 px-4 py-2">
			<span class="h-2 w-2 shrink-0 rounded-full bg-warning" aria-hidden="true"></span>
			<h2 id="needs-action-title" class="text-sm font-semibold text-gray-900 dark:text-white">
				Needs your action <span class="font-normal text-gray-500 dark:text-gray-400">· {count}</span
				>
			</h2>
			<div class="ml-auto flex items-center gap-1">
				<button
					type="button"
					onclick={onShowInTable}
					class="min-h-11 px-2 text-sm text-brand hover:underline dark:text-brand-muted"
					data-testid="needs-action-show"
				>
					Show in table
				</button>
				<button
					type="button"
					onclick={toggle}
					class="inline-flex min-h-11 min-w-11 items-center justify-center rounded-lg text-gray-500 hover:bg-surface-2 dark:text-gray-400"
					aria-expanded={!collapsed}
					aria-controls="needs-action-list"
					aria-label={collapsed ? 'Show the problems' : 'Hide the problems'}
					data-testid="needs-action-toggle"
				>
					<ChevronDown size={18} class="transition-transform {collapsed ? '' : 'rotate-180'}" />
				</button>
			</div>
		</div>

		{#if !collapsed}
			<ul
				id="needs-action-list"
				class="divide-y divide-gray-100 border-t border-gray-100 dark:divide-gray-700 dark:border-gray-700"
			>
				{#each lines as product (product.id)}
					{@const attention = product.attention!}
					{@const fix = fixAction(attention.kind, challengeAvailable)}
					{@const Icon = ICONS[attention.kind]}
					<li
						class="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2 sm:flex-nowrap"
						data-testid="needs-action-item"
					>
						<Icon size={18} class="shrink-0 text-warning" aria-hidden="true" />
						<div class="min-w-0 flex-1">
							<p class="text-sm text-gray-900 dark:text-white">{attentionCause(attention)}</p>
							<p class="truncate text-xs text-gray-500 dark:text-gray-400">
								<a
									href={resolve(`/products/${product.id}`)}
									class="hover:text-brand hover:underline dark:hover:text-brand-muted"
									>{product.name}</a
								>
								{#if hostOf(attention.url)}
									· {hostOf(attention.url)}{/if}
							</p>
						</div>
						{#if fix.kind === 'link'}
							<a
								href={resolve(`/products/${product.id}`)}
								class={outlineButton}
								data-testid="needs-action-fix">{fix.label}</a
							>
						{:else if fix.kind === 'challenge'}
							<button
								type="button"
								onclick={() => onSolveCheck(product)}
								class={outlineButton}
								data-testid="needs-action-fix">{fix.label}</button
							>
						{:else}
							<button
								type="button"
								onclick={() => retry(product)}
								disabled={busyId === product.id}
								class={outlineButton}
								data-testid="needs-action-fix"
							>
								{#if busyId === product.id}<LoaderCircle
										size={14}
										class="animate-spin"
										aria-hidden="true"
									/>{/if}
								{fix.label}
							</button>
						{/if}
					</li>
				{/each}
				{#if hidden > 0}
					<li
						class="px-4 py-2 text-sm text-gray-500 dark:text-gray-400"
						data-testid="needs-action-more"
					>
						{hidden} more —
						<button
							type="button"
							onclick={onShowInTable}
							class="text-brand hover:underline dark:text-brand-muted">show all in the table</button
						>
					</li>
				{/if}
			</ul>
		{/if}
	</section>
{/if}
