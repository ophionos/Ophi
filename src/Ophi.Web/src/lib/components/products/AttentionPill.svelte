<script lang="ts">
	import { resolve } from '$app/paths';
	import type { ProductAttention } from '$lib/api/client';
	import { attentionLabel } from '$lib/utils/watchlist';

	interface Props {
		attention: ProductAttention;
		productId: string;
	}

	let { attention, productId }: Props = $props();
</script>

<!-- Quiet on purpose: a light amber pill for a state the user can fix, a grey outline for
     information. The status column stays empty for a healthy product. -->
<span class="inline-flex flex-wrap items-center gap-x-2 gap-y-0.5" data-testid="attention-pill">
	{#if attention.isAction}
		<span
			class="inline-flex items-center rounded-full border border-warning/30 bg-warning/10 px-2 py-0.5 text-xs font-medium whitespace-nowrap text-warning"
			data-kind={attention.kind}
		>
			{attentionLabel(attention.kind)}
		</span>
		<a
			href={resolve(`/products/${productId}`)}
			class="text-xs text-brand hover:underline dark:text-brand-muted"
			data-testid="attention-fix-link"
		>
			{attention.kind === 'priceAnomaly' ? 'Review' : 'Fix'}
		</a>
	{:else}
		<span
			class="inline-flex items-center rounded-full border border-gray-300 px-2 py-0.5 text-xs whitespace-nowrap text-gray-600 dark:border-gray-600 dark:text-gray-400"
			data-kind={attention.kind}
		>
			{attentionLabel(attention.kind)}
		</span>
	{/if}
</span>
