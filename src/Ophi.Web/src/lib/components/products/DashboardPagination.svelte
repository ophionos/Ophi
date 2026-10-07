<script lang="ts">
	import { ChevronLeft, ChevronRight } from 'lucide-svelte';

	interface Props {
		currentPage: number;
		totalPages: number;
		onPageChange: (page: number) => void;
	}

	let { currentPage, totalPages, onPageChange }: Props = $props();
</script>

<div class="flex items-center justify-center gap-3 mt-6" data-testid="pagination-controls">
	<button
		onclick={() => onPageChange(Math.max(1, currentPage - 1))}
		disabled={currentPage <= 1}
		class="flex items-center gap-1 px-3 py-1.5 text-sm font-medium rounded-md transition-colors {currentPage <= 1 ? 'text-gray-400 dark:text-gray-600' : 'text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700'}"
		data-testid="pagination-prev"
	>
		<ChevronLeft size={16} />
		Previous
	</button>
	<span class="text-sm text-gray-600 dark:text-gray-400" data-testid="pagination-info">
		Page {currentPage} of {totalPages}
	</span>
	<button
		onclick={() => onPageChange(Math.min(totalPages, currentPage + 1))}
		disabled={currentPage >= totalPages}
		class="flex items-center gap-1 px-3 py-1.5 text-sm font-medium rounded-md transition-colors {currentPage >= totalPages ? 'text-gray-400 dark:text-gray-600' : 'text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700'}"
		data-testid="pagination-next"
	>
		Next
		<ChevronRight size={16} />
	</button>
</div>
