<script lang="ts">
	import { CircleCheckBig, CircleX, Info, X } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { fly, fade } from 'svelte/transition';
	import { flip } from 'svelte/animate';
</script>

<div class="fixed bottom-4 right-4 z-[200] flex flex-col gap-3 pointer-events-none" aria-live="polite" aria-label="Notifications">
	{#each toast.items as item (item.id)}
		<div
			animate:flip={{ duration: 250 }}
			in:fly={{ y: 12, duration: 200, opacity: 0 }}
			out:fade={{ duration: 150 }}
			class="flex items-start gap-3 px-4 py-3 text-sm pointer-events-auto max-w-sm rounded-lg shadow-lg bg-surface-1 border border-gray-200 dark:border-gray-700 text-gray-900 dark:text-gray-100"
			role="status"
		>
			{#if item.type === 'success'}
				<CircleCheckBig size={16} class="mt-0.5 shrink-0 text-success" />
			{:else if item.type === 'error'}
				<CircleX size={16} class="mt-0.5 shrink-0 text-danger" />
			{:else}
				<Info size={16} class="mt-0.5 shrink-0 text-brand dark:text-brand-muted" />
			{/if}
			<span class="flex-1">{item.message}</span>
			<button
				onclick={() => toast.dismiss(item.id)}
				class="tap-area shrink-0 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 transition-colors"
				aria-label="Dismiss"
			>
				<X size={14} />
			</button>
		</div>
	{/each}
</div>
