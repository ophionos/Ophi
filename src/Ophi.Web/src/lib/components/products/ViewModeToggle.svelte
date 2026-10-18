<script lang="ts">
	import { LayoutGrid, Table2 } from 'lucide-svelte';
	import type { ViewMode } from '$lib/stores/viewMode.svelte';

	interface Props {
		mode: ViewMode;
		onModeChange: (mode: ViewMode) => void;
	}

	let { mode, onModeChange }: Props = $props();

	const MODES = [
		{ value: 'list' as const, label: 'Table', icon: Table2 },
		{ value: 'grid' as const, label: 'Gallery', icon: LayoutGrid }
	];
</script>

<div class="flex items-center gap-0.5 rounded-lg bg-surface-2 p-0.5" role="group" aria-label="View" data-onboarding="view-toggles">
	{#each MODES as item (item.value)}
		{@const Icon = item.icon}
		<button
			type="button"
			onclick={() => onModeChange(item.value)}
			class="inline-flex min-h-11 items-center gap-1.5 rounded-md px-3 text-sm transition-colors {mode === item.value
				? 'bg-surface-1 text-brand shadow-sm dark:text-brand-muted'
				: 'text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-300'}"
			aria-pressed={mode === item.value}
			aria-label={item.label}
			data-testid="view-toggle-{item.value}"
		>
			<Icon size={16} aria-hidden="true" />
			<span class="hidden sm:inline" aria-hidden="true">{item.label}</span>
		</button>
	{/each}
</div>
