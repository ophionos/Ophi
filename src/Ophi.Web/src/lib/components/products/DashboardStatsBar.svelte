<script lang="ts">
	import { resolve } from '$app/paths';
	import { Package, TrendingDown, Bell, BadgeDollarSign } from 'lucide-svelte';

	type StatsFilter = null | 'drops' | 'alerts' | 'lowest';

	interface Props {
		totalProducts: number;
		priceDrops: number;
		alertCount: number;
		atLowestCount: number;
		statsFilter: StatsFilter;
		onFilterChange: (filter: StatsFilter) => void;
	}

	let { totalProducts, priceDrops, alertCount, atLowestCount, statsFilter, onFilterChange }: Props = $props();
</script>

<div class="flex flex-wrap items-center gap-x-4 gap-y-2 mb-4 px-1" data-testid="stats-bar" data-onboarding="stats-bar">
	<div class="flex items-center gap-2">
		<Package size={16} class="text-brand" />
		<span class="text-sm text-gray-500 dark:text-gray-400">Products</span>
		<span class="text-sm font-semibold text-gray-900 dark:text-white">{totalProducts}</span>
	</div>
	<div class="hidden sm:block w-px h-4 bg-gray-200 dark:bg-gray-700"></div>
	<button
		onclick={() => onFilterChange(statsFilter === 'drops' ? null : 'drops')}
		class="flex items-center gap-2 px-2 py-1 -mx-2 -my-1 rounded-md transition-colors {statsFilter === 'drops' ? 'bg-green-100 dark:bg-green-900/30' : 'hover:bg-gray-100 dark:hover:bg-gray-800'}"
		title={statsFilter === 'drops' ? 'Clear filter' : 'Filter: products with price drops'}
	>
		<TrendingDown size={16} class="text-green-500" />
		<span class="text-sm text-gray-500 dark:text-gray-400">Drops</span>
		<span class="text-sm font-semibold {statsFilter === 'drops' ? 'text-green-700 dark:text-green-400' : 'text-gray-900 dark:text-white'}">{priceDrops}</span>
	</button>
	<div class="hidden sm:block w-px h-4 bg-gray-200 dark:bg-gray-700"></div>
	<button
		onclick={() => onFilterChange(statsFilter === 'alerts' ? null : 'alerts')}
		class="flex items-center gap-2 px-2 py-1 -mx-2 -my-1 rounded-md transition-colors {statsFilter === 'alerts' ? 'bg-yellow-100 dark:bg-yellow-900/30' : 'hover:bg-gray-100 dark:hover:bg-gray-800'}"
		title={statsFilter === 'alerts' ? 'Clear filter' : 'Filter: products with alerts'}
	>
		<Bell size={16} class="text-yellow-500" />
		<span class="text-sm text-gray-500 dark:text-gray-400">Alerts</span>
		<span class="text-sm font-semibold {statsFilter === 'alerts' ? 'text-yellow-700 dark:text-yellow-400' : 'text-gray-900 dark:text-white'}">{alertCount}</span>
	</button>
	<a
		href={resolve('/alerts')}
		class="text-xs font-medium text-brand dark:text-brand-muted hover:underline"
		data-testid="stats-view-all-alerts"
	>
		View all
	</a>
	<div class="hidden sm:block w-px h-4 bg-gray-200 dark:bg-gray-700"></div>
	<button
		onclick={() => onFilterChange(statsFilter === 'lowest' ? null : 'lowest')}
		class="flex items-center gap-2 px-2 py-1 -mx-2 -my-1 rounded-md transition-colors {statsFilter === 'lowest' ? 'bg-brand-subtle/30 dark:bg-brand/10' : 'hover:bg-gray-100 dark:hover:bg-gray-800'}"
		title={statsFilter === 'lowest' ? 'Clear filter' : 'Filter: products at their lowest price'}
		data-testid="stats-filter-lowest"
	>
		<BadgeDollarSign size={16} class="text-brand" />
		<span class="text-sm text-gray-500 dark:text-gray-400">At Lowest</span>
		<span class="text-sm font-semibold {statsFilter === 'lowest' ? 'text-brand-hover dark:text-brand-muted' : 'text-gray-900 dark:text-white'}">{atLowestCount}</span>
	</button>
</div>
