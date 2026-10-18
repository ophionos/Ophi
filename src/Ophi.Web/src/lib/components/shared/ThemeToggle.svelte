<script lang="ts">
	import { Sun, Moon } from 'lucide-svelte';
	import { theme } from '$lib/stores/theme.svelte';
</script>

<button
	onclick={() => theme.toggle()}
	class="flex min-h-11 min-w-11 items-center justify-center text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700 rounded-full transition-colors"
	title={theme.current === 'light' ? 'Switch to dark mode' : 'Switch to light mode'}
	aria-label={theme.current === 'light' ? 'Switch to dark mode' : 'Switch to light mode'}
>
	{#if theme.current === 'light'}
		<Moon size={20} />
	{:else}
		<Sun size={20} />
	{/if}
</button>
