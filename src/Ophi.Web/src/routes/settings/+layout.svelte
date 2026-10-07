<script lang="ts">
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import { Settings, Bell, KeyRound, DatabaseBackup, UserRound } from 'lucide-svelte';

	interface Props {
		children?: import('svelte').Snippet;
	}

	let { children }: Props = $props();

	const sections = [
		{ href: resolve('/settings/scraping'), label: 'Scraping', icon: Settings },
		{ href: resolve('/settings/notifications'), label: 'Notifications', icon: Bell },
		{ href: resolve('/settings/api-keys'), label: 'API Keys', icon: KeyRound },
		{ href: resolve('/settings/data'), label: 'Data', icon: DatabaseBackup },
		{ href: resolve('/settings/account'), label: 'Account', icon: UserRound }
	];

	const currentPath = $derived(page.url.pathname);
</script>

<div>
	<div class="mb-6">
		<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">Settings</h1>
		<p class="text-gray-600 dark:text-gray-400">
			Scraping behaviour, notification channels, API access, data import/export, and your account
		</p>
	</div>

	<nav class="flex flex-wrap gap-2 mb-6 border-b border-gray-200 dark:border-gray-700 pb-px" aria-label="Settings sections" data-testid="settings-nav">
		{#each sections as section (section.href)}
			{@const active = currentPath.startsWith(section.href)}
			<a
				href={section.href}
				class="flex items-center gap-2 px-4 py-2 text-sm font-medium rounded-t-lg border-b-2 -mb-px transition-colors {active
					? 'text-brand dark:text-brand-muted border-brand'
					: 'text-gray-600 dark:text-gray-400 border-transparent hover:text-gray-900 dark:hover:text-gray-200 hover:bg-surface-2'}"
				aria-current={active ? 'page' : undefined}
			>
				<section.icon size={15} />
				{section.label}
			</a>
		{/each}
	</nav>

	{@render children?.()}
</div>
