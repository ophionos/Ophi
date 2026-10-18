<script lang="ts">
	import type { Snippet } from 'svelte';

	interface Props {
		message: string;
		/** `inline` is red text under a field or action; `box` is the tinted panel at the top of a form. */
		variant?: 'inline' | 'box';
		size?: 'sm' | 'xs';
		/** Put on the announced message so an input can point `aria-describedby` at it. */
		id?: string;
		testid?: string;
		class?: string;
		/** The wrapper element, for callers that scroll the error into view. */
		element?: HTMLDivElement;
		/** Extra controls (e.g. a retry button), rendered outside the announced region. */
		children?: Snippet;
	}

	let {
		message,
		variant = 'inline',
		size = 'sm',
		id,
		testid,
		class: className = '',
		element = $bindable(),
		children
	}: Props = $props();

	const variantClass = $derived(
		variant === 'box'
			? 'p-3 bg-danger/10 border border-danger/30 rounded-md'
			: ''
	);
</script>

<div
	bind:this={element}
	data-testid={testid}
	data-form-error={variant}
	class="{variantClass} {children ? 'flex items-center gap-2' : ''} {className}"
>
	<p role="alert" {id} class="{size === 'xs' ? 'text-xs' : 'text-sm'} text-danger">
		{message}
	</p>
	{@render children?.()}
</div>
