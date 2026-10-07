<script lang="ts">
	import '../app.css';
	import { onMount } from 'svelte';
	import { LoaderCircle, LogOut, Menu, X, ChevronDown, Tag, Store, Search, Activity, SlidersHorizontal, UserRound } from 'lucide-svelte';
	import OphiLogo from '$lib/components/shared/OphiLogo.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { theme } from '$lib/stores/theme.svelte';
	import { api } from '$lib/api/client';
	import { goto, onNavigate } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { page, navigating } from '$app/state';
	import ThemeToggle from '$lib/components/shared/ThemeToggle.svelte';
	import NotificationBell from '$lib/components/shared/NotificationBell.svelte';
	import CommandPalette from '$lib/components/shared/CommandPalette.svelte';
	import Toaster from '$lib/components/shared/Toaster.svelte';
	import { notifications } from '$lib/stores/notifications.svelte';
	import { liveUpdates } from '$lib/stores/liveUpdates.svelte';
	import { products } from '$lib/stores/products.svelte';
	import { tags } from '$lib/stores/tags.svelte';
	import { comparisons } from '$lib/stores/comparisons.svelte';
	import { stores } from '$lib/stores/stores.svelte';
	import SkipLink from '$lib/components/shared/SkipLink.svelte';
	import { menuKeyNav } from '$lib/actions/menuKeyNav';
	import InstallPrompt from '$lib/components/shared/InstallPrompt.svelte';
	import NavigationProgress from '$lib/components/shared/NavigationProgress.svelte';
	import { fx } from '$lib/stores/fx.svelte';

	interface Props {
		children?: import('svelte').Snippet;
		data: {
			user: { id: string; email: string; name: string } | null;
			authChecked: boolean;
			isPublic: boolean;
		};
	}

	let props: Props = $props();
	const data = $derived(props.data);
	const children = $derived(props.children);
	let mobileMenuOpen = $state(false);
	let clientAuthChecked = $state(false);
	let clientAuthChecking = $state(false);
	let settingsOpen = $state(false);
	let userMenuOpen = $state(false);
	let commandPaletteOpen = $state(false);

	const currentPath = $derived(page.url.pathname);

	function isActive(resolved: string): boolean {
		if (resolved === resolve('/dashboard')) return currentPath === resolved || currentPath === resolve('/');
		return currentPath.startsWith(resolved);
	}

	function navLinkClass(resolved: string): string {
		return isActive(resolved)
			? 'text-brand dark:text-brand-muted bg-brand-light dark:bg-brand-dark/30'
			: 'text-gray-700 dark:text-gray-300 hover:text-brand dark:hover:text-brand-muted hover:bg-surface-2';
	}

	const shouldCheckAuth = $derived(!data.isPublic && !data.authChecked);
	const authChecked = $derived(data.authChecked || clientAuthChecked);
	const checkingAuth = $derived(shouldCheckAuth && (clientAuthChecking || !clientAuthChecked));

	function getUserInitial(): string {
		if (!data.user?.name) return '?';
		return data.user.name.charAt(0).toUpperCase();
	}

	function handleClickOutside(e: MouseEvent) {
		const target = e.target as HTMLElement;
		if (!target.closest('[data-settings-menu]')) {
			settingsOpen = false;
		}
		if (!target.closest('[data-user-menu]')) {
			userMenuOpen = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			settingsOpen = false;
			userMenuOpen = false;
		}
		if (e.key === 'k' && (e.metaKey || e.ctrlKey)) {
			e.preventDefault();
			commandPaletteOpen = !commandPaletteOpen;
		}
	}

	onNavigate((navigation) => {
		if (!document.startViewTransition) return;

		return new Promise((resolve) => {
			document.startViewTransition(async () => {
				resolve();
				await navigation.complete;
			});
		});
	});

	onMount(() => {
		// Hydration marker for Playwright: fills/clicks that land before hydration hit the
		// native form/anchor behavior instead of the JS handlers. `networkidle` can't serve
		// as the wait — the app-wide SSE connection keeps the network permanently busy.
		document.body.dataset.hydrated = 'true';

		// Initialize theme
		theme.init();

		document.addEventListener('click', handleClickOutside);
		document.addEventListener('keydown', handleKeydown);

		return () => {
			document.removeEventListener('click', handleClickOutside);
			document.removeEventListener('keydown', handleKeydown);
		};
	});

	$effect(() => {
		if (!shouldCheckAuth) return;

		const checkAuth = async () => {
			clientAuthChecking = true;

			try {
				const user = await api.me();
				auth.setUser(user);
				clientAuthChecked = true;
			} catch {
				goto(resolve('/auth/login'));
			} finally {
				clientAuthChecking = false;
			}
		};

		checkAuth();
	});

	$effect(() => {
		auth.setUser(data.user);
	});

	// One app-wide SSE connection while signed in; surfaces subscribe via the liveUpdates store.
	$effect(() => {
		if (data.user) {
			liveUpdates.connect();
			fx.load();
		} else {
			liveUpdates.disconnect();
		}
	});

	async function handleLogout() {
		try {
			await api.logout();
			auth.logout();
			liveUpdates.disconnect();
			fx.reset();
			notifications.clear();
			products.clear();
			tags.clear();
			comparisons.clear();
			stores.clear();
			goto(resolve('/auth/login'));
		} catch (error) {
			console.error('Logout failed:', error);
		}
	}
</script>

<svelte:head>
	<title>Ophi - Price Tracker</title>
</svelte:head>

<SkipLink />
<NavigationProgress active={navigating.to !== null} />
<div class="min-h-screen bg-surface-0 transition-colors">
	{#if data.user && authChecked}
		<nav class="sticky top-0 z-50 bg-surface-1/80 backdrop-blur-md border-b border-gray-200 dark:border-gray-700/50 shadow-sm" aria-label="Main navigation">
			<div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
				<div class="flex justify-between h-14">
					<div class="flex items-center">
						<!-- Mobile menu button -->
						<button
							onclick={() => (mobileMenuOpen = !mobileMenuOpen)}
							class="md:hidden p-2 -ml-2 mr-2 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-md"
							aria-label="Toggle menu"
							aria-expanded={mobileMenuOpen}
						>
							{#if mobileMenuOpen}
								<X size={24} />
							{:else}
								<Menu size={24} />
							{/if}
						</button>
						<a
							href={resolve('/dashboard')}
							class="flex items-center gap-2 text-xl font-bold text-brand dark:text-brand-muted"
						>
							<OphiLogo size={28} class="rounded" />
						</a>
						<div class="hidden md:flex ml-8 space-x-1">
							<a
								href={resolve('/dashboard')}
								class="px-3 py-1.5 text-sm font-medium {navLinkClass(resolve('/dashboard'))} rounded-md transition-colors"
								aria-current={isActive(resolve('/dashboard')) ? 'page' : undefined}
							>
								Dashboard
							</a>
							<a
								href={resolve('/comparisons')}
								class="px-3 py-1.5 text-sm font-medium {navLinkClass(resolve('/comparisons'))} rounded-md transition-colors"
								aria-current={isActive(resolve('/comparisons')) ? 'page' : undefined}
							>
								Comparisons
							</a>
							<a
								href={resolve('/alerts')}
								class="px-3 py-1.5 text-sm font-medium {navLinkClass(resolve('/alerts'))} rounded-md transition-colors"
								aria-current={isActive(resolve('/alerts')) ? 'page' : undefined}
							>
								Alerts
							</a>
							<!-- Settings dropdown -->
							<div class="relative" data-settings-menu use:menuKeyNav>
								<button
									onclick={() => (settingsOpen = !settingsOpen)}
									class="px-3 py-1.5 text-sm font-medium {isActive(resolve('/settings')) || isActive(resolve('/tags')) || isActive(resolve('/stores')) || isActive(resolve('/scrape-health')) ? 'text-brand dark:text-brand-muted bg-brand-light dark:bg-brand-dark/30' : 'text-gray-700 dark:text-gray-300 hover:text-brand dark:hover:text-brand-muted hover:bg-surface-2'} rounded-md transition-colors flex items-center gap-1"
									aria-expanded={settingsOpen}
									aria-haspopup="true"
								>
									Settings
									<ChevronDown size={12} class="transition-transform {settingsOpen ? 'rotate-180' : ''}" />
								</button>
								{#if settingsOpen}
									<div class="absolute left-0 mt-1 w-48 bg-surface-1 rounded-lg shadow-lg border border-gray-200 dark:border-gray-700 py-1 z-50">
										<a
											href={resolve('/settings')}
											onclick={() => (settingsOpen = false)}
											class="flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-surface-2 hover:text-brand dark:hover:text-brand-muted transition-colors"
										>
											<SlidersHorizontal size={15} class="text-gray-400 dark:text-gray-500" />
											App Settings
										</a>
										<a
											href={resolve('/tags')}
											onclick={() => (settingsOpen = false)}
											class="flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-surface-2 hover:text-brand dark:hover:text-brand-muted transition-colors"
										>
											<Tag size={15} class="text-gray-400 dark:text-gray-500" />
											Tags
										</a>
										<a
											href={resolve('/stores')}
											onclick={() => (settingsOpen = false)}
											class="flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-surface-2 hover:text-brand dark:hover:text-brand-muted transition-colors"
										>
											<Store size={15} class="text-gray-400 dark:text-gray-500" />
											Stores
										</a>
										<a
											href={resolve('/scrape-health')}
											onclick={() => (settingsOpen = false)}
											class="flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-surface-2 hover:text-brand dark:hover:text-brand-muted transition-colors"
										>
											<Activity size={15} class="text-gray-400 dark:text-gray-500" />
											Scrape Health
										</a>
									</div>
								{/if}
							</div>
						</div>
					</div>
					<div class="flex items-center gap-3">
						<button
							onclick={() => (commandPaletteOpen = true)}
							class="hidden md:flex items-center gap-1.5 px-2.5 py-1 text-xs text-gray-400 dark:text-gray-500 border border-gray-200 dark:border-gray-600 rounded-md hover:border-gray-300 dark:hover:border-gray-500 hover:text-gray-600 dark:hover:text-gray-400 transition-colors"
							aria-label="Open command palette"
							data-command-palette-trigger
						>
							<Search size={13} />
							<span>Search</span>
							<kbd class="ml-1 text-[10px] font-medium opacity-60">&#x2318;K</kbd>
						</button>
						<ThemeToggle />
						<NotificationBell />
						<!-- User menu dropdown -->
						<div class="relative hidden md:block" data-user-menu use:menuKeyNav>
							<button
								onclick={() => (userMenuOpen = !userMenuOpen)}
								class="flex items-center gap-2 p-1 rounded-full hover:bg-surface-2 transition-colors"
								aria-label="User menu"
								aria-expanded={userMenuOpen}
								aria-haspopup="true"
							>
								<div class="w-7 h-7 rounded-full bg-brand text-white text-xs font-semibold flex items-center justify-center">
									{getUserInitial()}
								</div>
								<ChevronDown size={12} class="text-gray-500 dark:text-gray-400 transition-transform {userMenuOpen ? 'rotate-180' : ''}" />
							</button>
							{#if userMenuOpen}
								<div class="absolute right-0 mt-1 w-48 bg-surface-1 rounded-lg shadow-lg border border-gray-200 dark:border-gray-700 py-1 z-50">
									<div class="px-4 py-2 border-b border-gray-200 dark:border-gray-700">
										<p class="text-sm font-medium text-gray-900 dark:text-white truncate">{data.user.name}</p>
										<p class="text-xs text-gray-500 dark:text-gray-400 truncate">{data.user.email}</p>
									</div>
									<a
										href={resolve('/settings/account')}
										onclick={() => (userMenuOpen = false)}
										class="flex items-center gap-2 px-4 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-surface-2 hover:text-brand dark:hover:text-brand-muted transition-colors"
									>
										<UserRound size={14} />
										Account
									</a>
									<button
										onclick={() => { userMenuOpen = false; handleLogout(); }}
										class="w-full text-left px-4 py-2 text-sm text-red-600 dark:text-red-400 hover:bg-surface-2 transition-colors flex items-center gap-2"
									>
										<LogOut size={14} />
										Logout
									</button>
								</div>
							{/if}
						</div>
					</div>
				</div>
			</div>

			<!-- Mobile menu -->
			{#if mobileMenuOpen}
				<div class="md:hidden border-t border-gray-200 dark:border-gray-700">
					<div class="px-4 py-3 space-y-1">
						<a
							href={resolve('/dashboard')}
							onclick={() => (mobileMenuOpen = false)}
							class="block px-3 py-2 text-base font-medium {navLinkClass(resolve('/dashboard'))} rounded-md"
							aria-current={isActive(resolve('/dashboard')) ? 'page' : undefined}
						>
							Dashboard
						</a>
						<a
							href={resolve('/comparisons')}
							onclick={() => (mobileMenuOpen = false)}
							class="block px-3 py-2 text-base font-medium {navLinkClass(resolve('/comparisons'))} rounded-md"
							aria-current={isActive(resolve('/comparisons')) ? 'page' : undefined}
						>
							Comparisons
						</a>
						<a
							href={resolve('/alerts')}
							onclick={() => (mobileMenuOpen = false)}
							class="block px-3 py-2 text-base font-medium {navLinkClass(resolve('/alerts'))} rounded-md"
							aria-current={isActive(resolve('/alerts')) ? 'page' : undefined}
						>
							Alerts
						</a>
						<div class="pt-2 mt-2 border-t border-gray-200 dark:border-gray-700">
							<p class="px-3 py-1 text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wider">Settings</p>
						</div>
						<a
							href={resolve('/settings')}
							onclick={() => (mobileMenuOpen = false)}
							class="flex items-center gap-2.5 px-3 py-2 text-base font-medium {navLinkClass(resolve('/settings'))} rounded-md"
							aria-current={isActive(resolve('/settings')) ? 'page' : undefined}
						>
							<SlidersHorizontal size={18} class="text-gray-400 dark:text-gray-500" />
							App Settings
						</a>
						<a
							href={resolve('/tags')}
							onclick={() => (mobileMenuOpen = false)}
							class="flex items-center gap-2.5 px-3 py-2 text-base font-medium {navLinkClass(resolve('/tags'))} rounded-md"
							aria-current={isActive(resolve('/tags')) ? 'page' : undefined}
						>
							<Tag size={18} class="text-gray-400 dark:text-gray-500" />
							Tags
						</a>
						<a
							href={resolve('/stores')}
							onclick={() => (mobileMenuOpen = false)}
							class="flex items-center gap-2.5 px-3 py-2 text-base font-medium {navLinkClass(resolve('/stores'))} rounded-md"
							aria-current={isActive(resolve('/stores')) ? 'page' : undefined}
						>
							<Store size={18} class="text-gray-400 dark:text-gray-500" />
							Stores
						</a>
						<a
							href={resolve('/scrape-health')}
							onclick={() => (mobileMenuOpen = false)}
							class="flex items-center gap-2.5 px-3 py-2 text-base font-medium {navLinkClass(resolve('/scrape-health'))} rounded-md"
							aria-current={isActive(resolve('/scrape-health')) ? 'page' : undefined}
						>
							<Activity size={18} class="text-gray-400 dark:text-gray-500" />
							Scrape Health
						</a>
					</div>
					<div class="border-t border-gray-200 dark:border-gray-700 px-4 py-3">
						<div class="flex items-center justify-between">
							<div class="flex items-center gap-3">
								<div class="w-8 h-8 rounded-full bg-brand text-white text-sm font-semibold flex items-center justify-center">
									{getUserInitial()}
								</div>
								<span class="text-sm font-medium text-gray-700 dark:text-gray-300"
									>{data.user.name}</span
								>
							</div>
							<button
								onclick={() => {
									mobileMenuOpen = false;
									handleLogout();
								}}
								class="flex items-center gap-2 px-3 py-2 text-sm font-medium text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md"
							>
								<LogOut size={18} />
								Logout
							</button>
						</div>
					</div>
				</div>
			{/if}
		</nav>
	{/if}

	{#if data.user && authChecked}
		<CommandPalette isOpen={commandPaletteOpen} onClose={() => (commandPaletteOpen = false)} />
	{/if}

	<Toaster />
	{#if data.user}
		<!-- Signed-in only: asking a first-time visitor to install the app before they have an
		     account put the banner over the marketing page, register and login. -->
		<InstallPrompt />
	{/if}

	{#if checkingAuth}
		<div class="flex items-center justify-center min-h-screen">
			<LoaderCircle size={32} class="animate-spin text-gray-400" />
		</div>
	{:else}
		<main id="main-content" class={data.user ? 'max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6' : ''}>
			{@render children?.()}
		</main>
	{/if}
</div>
