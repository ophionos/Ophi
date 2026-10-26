<script lang="ts">
	import { CircleMinus, TriangleAlert } from 'lucide-svelte';
	import { resolve } from '$app/paths';
	import type { ProductAttention } from '$lib/api/client';
	import { attentionLabel } from '$lib/utils/watchlist';

	interface Props {
		attention: ProductAttention;
		productId: string;
	}

	let { attention, productId }: Props = $props();

	const label = $derived(attentionLabel(attention.kind));
</script>

<!-- Icon only, so the name keeps its room: amber for a state the user can fix, gray for
     information. The label is the tooltip and the accessible name. -->
<a
	href={resolve(`/products/${productId}`)}
	class="inline-flex shrink-0 items-center {attention.isAction ? 'text-warning' : 'text-gray-400 dark:text-gray-500'}"
	title={label}
	aria-label={label}
	data-testid="attention-icon"
	data-kind={attention.kind}
>
	{#if attention.isAction}
		<TriangleAlert size={14} aria-hidden="true" />
	{:else}
		<CircleMinus size={14} aria-hidden="true" />
	{/if}
</a>
