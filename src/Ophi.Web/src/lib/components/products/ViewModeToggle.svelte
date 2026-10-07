<script lang="ts">
	import { LayoutGrid, List, Activity } from 'lucide-svelte';

	type ViewMode = 'grid' | 'list' | 'feed';

	interface Props {
		mode: ViewMode;
		onModeChange: (mode: ViewMode) => void;
	}

	let { mode, onModeChange }: Props = $props();
</script>

<div class="flex items-center gap-1 bg-gray-100 dark:bg-gray-700 rounded-lg p-0.5" data-onboarding="view-toggles">
	<button
		onclick={() => onModeChange('grid')}
		class="p-1.5 rounded-md transition-colors {mode === 'grid' ? 'bg-white dark:bg-gray-600 text-brand dark:text-brand-muted shadow-sm' : 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'}"
		aria-label="Grid view"
		aria-pressed={mode === 'grid'}
		data-testid="view-toggle-grid"
	>
		<LayoutGrid size={16} />
	</button>
	<button
		onclick={() => onModeChange('list')}
		class="p-1.5 rounded-md transition-colors {mode === 'list' ? 'bg-white dark:bg-gray-600 text-brand dark:text-brand-muted shadow-sm' : 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'}"
		aria-label="List view"
		aria-pressed={mode === 'list'}
		data-testid="view-toggle-list"
	>
		<List size={16} />
	</button>
	<button
		onclick={() => onModeChange('feed')}
		class="p-1.5 rounded-md transition-colors {mode === 'feed' ? 'bg-white dark:bg-gray-600 text-brand dark:text-brand-muted shadow-sm' : 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'}"
		aria-label="Activity feed"
		aria-pressed={mode === 'feed'}
		data-testid="view-toggle-feed"
	>
		<Activity size={16} />
	</button>
</div>
