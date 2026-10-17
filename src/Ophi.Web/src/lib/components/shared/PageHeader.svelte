<script lang="ts">
	import type { Snippet } from 'svelte';

	// The one page title pattern: every authenticated route starts with this, so titles, spacing
	// and the action row line up from page to page.
	interface Props {
		title: string;
		/** Optional line under the title. */
		description?: string;
		/** Optional buttons on the right; they wrap under the title on a narrow screen. */
		actions?: Snippet;
	}

	let { title, description, actions }: Props = $props();
</script>

<div class="mb-6 flex flex-wrap items-start justify-between gap-4" data-testid="page-header">
	<div class="min-w-0">
		<h1 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">{title}</h1>
		{#if description}
			<p class="mt-1 text-gray-600 dark:text-gray-400">{description}</p>
		{/if}
	</div>
	{#if actions}
		<div class="flex shrink-0 flex-wrap gap-2">
			{@render actions()}
		</div>
	{/if}
</div>
