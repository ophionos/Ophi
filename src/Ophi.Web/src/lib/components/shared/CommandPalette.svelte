<script lang="ts">
	import { Search, LayoutDashboard, Tag, Store, Package, X, Zap } from 'lucide-svelte';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { api } from '$lib/api/client';
	import { theme } from '$lib/stores/theme.svelte';
	import { fade, fly } from 'svelte/transition';
	import { focusTrap } from '$lib/actions/focusTrap';

	interface CommandItem {
		id: string;
		label: string;
		description?: string;
		/** Navigation target; ignored when `action` is set. */
		href?: string;
		/** Runs instead of navigating (e.g. toggle theme). */
		action?: () => void;
		section: 'Pages' | 'Actions' | 'Products' | 'Tags' | 'Stores';
	}

	interface Props {
		isOpen: boolean;
		onClose: () => void;
	}

	let { isOpen, onClose }: Props = $props();

	let query = $state('');
	let selectedIndex = $state(0);
	let searchResults = $state<CommandItem[]>([]);
	let searching = $state(false);
	let searchError = $state(false);
	let searchInput: HTMLInputElement | undefined = $state();
	let debounceTimer: ReturnType<typeof setTimeout> | undefined;
	let searchSeq = 0;

	const staticItems: CommandItem[] = [
		{ id: 'nav-dashboard', label: 'Dashboard', description: 'View your products', href: resolve('/dashboard'), section: 'Pages' },
		{ id: 'nav-comparisons', label: 'Comparisons', description: 'Compare product prices', href: resolve('/comparisons'), section: 'Pages' },
		{ id: 'nav-alerts', label: 'Alerts', description: 'Every price alert in one place', href: resolve('/alerts'), section: 'Pages' },
		{ id: 'nav-settings', label: 'Settings', description: 'Scraping, notifications, API keys, data', href: resolve('/settings'), section: 'Pages' },
		{ id: 'nav-tags', label: 'Tags', description: 'Manage product tags', href: resolve('/tags'), section: 'Pages' },
		{ id: 'nav-stores', label: 'Stores', description: 'Manage store configurations', href: resolve('/stores'), section: 'Pages' },
		{ id: 'action-add-product', label: 'Add product', description: 'Jump to the dashboard URL box', action: addProductAction, section: 'Actions' },
		{ id: 'action-toggle-theme', label: 'Toggle theme', description: 'Switch between light and dark', action: () => theme.toggle(), section: 'Actions' }
	];

	// Best-effort focus: the dashboard may still be mounting right after navigation.
	function focusAddProductInput(attempts = 20) {
		const input = document.querySelector<HTMLInputElement>('[data-onboarding="add-product"] input');
		if (input) {
			input.focus();
			return;
		}
		if (attempts > 0) requestAnimationFrame(() => focusAddProductInput(attempts - 1));
	}

	async function addProductAction() {
		await goto(resolve('/dashboard'));
		focusAddProductInput();
	}

	const filteredStatic = $derived(
		query.trim() === ''
			? staticItems
			: staticItems.filter((item) =>
					item.label.toLowerCase().includes(query.toLowerCase())
				)
	);

	const allItems = $derived([...filteredStatic, ...searchResults]);

	const groupedItems = $derived.by(() => {
		const groups: Record<string, CommandItem[]> = {};
		for (const item of allItems) {
			if (!groups[item.section]) groups[item.section] = [];
			groups[item.section].push(item);
		}
		return groups;
	});

	// Reset state when opened/closed
	$effect(() => {
		if (isOpen) {
			query = '';
			selectedIndex = 0;
			searchResults = [];
			searching = false;
			searchError = false;
			requestAnimationFrame(() => {
				searchInput?.focus();
			});
		}
	});

	// Debounced search
	$effect(() => {
		const q = query.trim();
		if (debounceTimer) clearTimeout(debounceTimer);

		if (q.length < 2) {
			searchResults = [];
			searching = false;
			return;
		}

		searching = true;
		searchError = false;
		const seq = ++searchSeq;
		debounceTimer = setTimeout(async () => {
			const [productsSettled, tagsSettled, storesSettled] = await Promise.allSettled([
				api.getProducts({ search: q }),
				api.getTags(q),
				api.getStores(q)
			]);

			// Discard stale results if a newer search was issued
			if (seq !== searchSeq) return;

			searching = false;

			if ([productsSettled, tagsSettled, storesSettled].every((r) => r.status === 'rejected')) {
				searchError = true;
				searchResults = [];
				return;
			}

			const results: CommandItem[] = [];

			if (productsSettled.status === 'fulfilled') {
				for (const p of productsSettled.value.items.slice(0, 5)) {
					results.push({
						id: `product-${p.id}`,
						label: p.name,
						description: p.url,
						href: resolve(`/products/${p.id}`),
						section: 'Products'
					});
				}
			}

			if (tagsSettled.status === 'fulfilled') {
				for (const t of tagsSettled.value.items.slice(0, 5)) {
					results.push({
						id: `tag-${t.id}`,
						label: t.name,
						description: `${t.productCount} product${t.productCount !== 1 ? 's' : ''}`,
						href: `${resolve('/dashboard')}?tag=${t.id}`,
						section: 'Tags'
					});
				}
			}

			if (storesSettled.status === 'fulfilled') {
				for (const s of storesSettled.value.items.slice(0, 5)) {
					// Editable stores deep-link straight into their edit modal via the existing
					// ?edit=<domain> mechanism; built-ins have no editor, so land on the list.
					results.push({
						id: `store-${s.storeId}`,
						label: s.name,
						description: s.domainPatterns.join(', '),
						href:
							!s.isBuiltIn && s.domainPatterns.length > 0
								? `${resolve('/stores')}?edit=${encodeURIComponent(s.domainPatterns[0])}`
								: resolve('/stores'),
						section: 'Stores'
					});
				}
			}

			searchResults = results;
		}, 200);

		return () => {
			if (debounceTimer) clearTimeout(debounceTimer);
		};
	});

	// Reset selectedIndex when items change
	$effect(() => {
		allItems;
		selectedIndex = 0;
	});

	function handleKeydown(e: KeyboardEvent) {
		// This listener lives on window for the palette's whole lifetime (the layout keeps the
		// component mounted); without this guard it cancels Enter/Escape/arrows app-wide while
		// closed — swallowing form submits and caret movement. Cmd/Ctrl+K opening is handled in
		// +layout.svelte, so nothing global belongs here.
		if (!isOpen) return;

		if (e.key === 'Escape') {
			e.preventDefault();
			onClose();
			return;
		}

		if (e.key === 'ArrowDown') {
			e.preventDefault();
			selectedIndex = allItems.length > 0 ? (selectedIndex + 1) % allItems.length : 0;
			return;
		}

		if (e.key === 'ArrowUp') {
			e.preventDefault();
			selectedIndex = allItems.length > 0 ? (selectedIndex - 1 + allItems.length) % allItems.length : 0;
			return;
		}

		if (e.key === 'Enter') {
			e.preventDefault();
			const item = allItems[selectedIndex];
			if (item) {
				navigateTo(item);
			}
			return;
		}
	}

	function navigateTo(item: CommandItem) {
		if (item.action) {
			item.action();
		} else if (item.href) {
			goto(item.href);
		}
		onClose();
	}

	function getIcon(section: CommandItem['section']) {
		switch (section) {
			case 'Pages':
				return LayoutDashboard;
			case 'Actions':
				return Zap;
			case 'Products':
				return Package;
			case 'Tags':
				return Tag;
			case 'Stores':
				return Store;
			default:
				return Search;
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if isOpen}
	<div class="fixed inset-0 z-50 overflow-y-auto">
		<!-- Backdrop -->
		<div
			class="fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
			transition:fade={{ duration: 200 }}
			onclick={onClose}
			role="presentation"
		></div>

		<!-- Palette -->
		<div class="relative flex justify-center pt-[15vh] px-4">
			<div
				class="w-full max-w-xl bg-white/95 dark:bg-gray-800/90 backdrop-blur-xl rounded-2xl shadow-2xl border border-white/20 dark:border-white/10 overflow-hidden ring-1 ring-black/5"
				in:fly={{ y: 10, duration: 300, delay: 50 }}
				out:fade={{ duration: 150 }}
				role="dialog"
				aria-modal="true"
				aria-label="Command palette"
				use:focusTrap
			>
				<!-- Search input -->
				<div class="flex items-center gap-3 px-4 border-b border-gray-200/50 dark:border-gray-700/50">
					<Search size={18} class="text-gray-400 shrink-0" />
					<input
						bind:this={searchInput}
						bind:value={query}
						type="text"
						placeholder="Search products, pages, tags..."
						class="w-full py-3 bg-transparent text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 outline-none text-sm"
					/>
					<button
						onclick={onClose}
						class="shrink-0 p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 rounded"
						aria-label="Close command palette"
					>
						<kbd class="hidden sm:inline-flex items-center px-1.5 py-0.5 text-[10px] font-medium text-gray-400 dark:text-gray-500 bg-gray-100 dark:bg-gray-700 rounded border border-gray-200 dark:border-gray-600">Esc</kbd>
						<span class="sm:hidden"><X size={16} /></span>
					</button>
				</div>

				<!-- Results -->
				<div class="max-h-72 overflow-y-auto py-2" role="listbox" aria-label="Search results">
					{#if searchError && query.trim().length >= 2}
						<div role="status" class="px-4 py-8 text-center text-sm text-red-500 dark:text-red-400">
							Search unavailable — check your connection
						</div>
					{:else if allItems.length === 0 && !searching}
						<div class="px-4 py-8 text-center text-sm text-gray-500 dark:text-gray-400">
							No results found
						</div>
					{:else if searching && searchResults.length === 0 && filteredStatic.length === 0}
						<div class="px-4 py-8 text-center text-sm text-gray-500 dark:text-gray-400">
							Searching...
						</div>
					{:else}
						{#each Object.entries(groupedItems) as [section, items] (section)}
							<div class="px-3 pt-2 pb-1">
								<span class="text-[11px] font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wider">{section}</span>
							</div>
							{#each items as item (item.id)}
								{@const globalIndex = allItems.indexOf(item)}
								{@const IconComponent = getIcon(item.section)}
								<div class="px-2 mb-0.5">
									<button
										class="w-full flex items-center gap-3 px-3 py-2.5 text-left text-sm transition-all duration-200 rounded-lg {globalIndex === selectedIndex ? 'bg-brand text-white shadow-md' : 'text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700/50'}"
										onclick={() => navigateTo(item)}
										onmouseenter={() => (selectedIndex = globalIndex)}
										role="option"
										aria-selected={globalIndex === selectedIndex}
										data-selected={globalIndex === selectedIndex}
									>
										<IconComponent size={16} class="shrink-0 {globalIndex === selectedIndex ? 'opacity-100 text-brand-subtle' : 'opacity-60'}" />
										<div class="flex-1 min-w-0">
											<span class="font-medium">{item.label}</span>
											{#if item.description}
												<span class="ml-2 text-xs truncate {globalIndex === selectedIndex ? 'text-brand-muted' : 'text-gray-400 dark:text-gray-500'}">{item.description}</span>
											{/if}
										</div>
									</button>
								</div>
							{/each}
						{/each}
					{/if}
				</div>
			</div>
		</div>
	</div>
{/if}
