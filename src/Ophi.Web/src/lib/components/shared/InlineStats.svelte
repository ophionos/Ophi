<script lang="ts">
	import type { ComponentType, SvelteComponent } from 'svelte';
	import type { IconProps } from 'lucide-svelte';

	// Counts are shown as one slim row; StatCard is kept for pages where the numbers are the
	// main content (product price stats, scrape health).
	type IconComponent = ComponentType<SvelteComponent<IconProps>>;

	interface Stat {
		label: string;
		value: string | number;
		icon?: IconComponent;
		testid?: string;
	}

	let { stats }: { stats: Stat[] } = $props();
</script>

<div class="flex flex-wrap items-center gap-x-4 gap-y-2 mb-6 px-1">
	{#each stats as stat, i (stat.label)}
		{#if i > 0}
			<div class="hidden sm:block w-px h-4 bg-gray-200 dark:bg-gray-700" data-divider></div>
		{/if}
		<div class="flex items-center gap-2" data-testid={stat.testid}>
			{#if stat.icon}
				{@const Icon = stat.icon}
				<Icon size={16} class="text-brand dark:text-brand-muted" />
			{/if}
			<span class="text-sm text-gray-500 dark:text-gray-400">{stat.label}</span>
			<span class="text-sm font-semibold tabular-nums text-gray-900 dark:text-white">{stat.value}</span>
		</div>
	{/each}
</div>
