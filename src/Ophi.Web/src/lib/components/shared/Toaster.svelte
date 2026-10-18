<script lang="ts">
	import { CircleCheckBig, CircleX, Info, X } from 'lucide-svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { fly, fade } from 'svelte/transition';
	import { flip } from 'svelte/animate';
</script>

<div class="fixed bottom-4 right-4 z-[200] flex flex-col gap-3 pointer-events-none" aria-live="polite" aria-label="Notifications">
	{#each toast.items as item (item.id)}
		<div
			animate:flip={{ duration: 400 }}
			in:fly={{ y: 20, duration: 400, opacity: 0 }}
			out:fade={{ duration: 200 }}
			class="flex items-start gap-3 px-4 py-3 border text-sm pointer-events-auto max-w-sm rounded-xl shadow-xl backdrop-blur-xl ring-1 ring-black/5
				{item.type === 'success' ? 'bg-success/10 border-success/20 text-success' : ''}
				{item.type === 'error' ? 'bg-danger/10 border-danger/20 text-danger' : ''}
				{item.type === 'info' ? 'bg-brand/10 dark:bg-brand/10 border-brand/20 text-brand-hover dark:text-brand-muted' : ''}"
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
				class="shrink-0 opacity-60 hover:opacity-100 transition-opacity"
				aria-label="Dismiss"
			>
				<X size={14} />
			</button>
		</div>
	{/each}
</div>
