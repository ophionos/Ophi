<script lang="ts">
	import { focusTrap } from './focusTrap';

	interface Props {
		showFocusable?: boolean;
	}

	let { showFocusable = true }: Props = $props();
</script>

<div use:focusTrap data-testid="trap-container">
	{#if showFocusable}
		<button data-testid="btn-first">First</button>
		<input data-testid="input-middle" />
		<button data-testid="btn-last">Last</button>
	{:else}
		<span>No focusable elements</span>
	{/if}
</div>
