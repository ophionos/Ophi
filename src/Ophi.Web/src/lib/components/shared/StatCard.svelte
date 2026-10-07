<script lang="ts">
	import type { ComponentType, SvelteComponent, Snippet } from 'svelte';
	import type { IconProps } from 'lucide-svelte';

	// lucide-svelte icons are legacy (SvelteComponentTyped) class components, so type the prop
	// with the legacy ComponentType wrapper rather than the Svelte 5 `Component` function type.
	type IconComponent = ComponentType<SvelteComponent<IconProps>>;

	interface Props {
		/** Uppercase label rendered above the value. */
		label: string;
		/** The stat value. Ignored when a `children` body is provided. */
		value?: string | number;
		/** Tailwind classes for the value text (e.g. a threshold color). */
		valueClass?: string;
		/** Optional small lucide icon rendered before the label. */
		icon?: IconComponent;
		/** Tailwind classes for the icon. */
		iconClass?: string;
		/** Custom body, rendered in place of the default value text (e.g. a multi-state cell). */
		children?: Snippet;
		testid?: string;
	}

	let {
		label,
		value,
		valueClass = 'text-gray-900 dark:text-white',
		icon,
		iconClass = 'text-gray-400 dark:text-gray-500',
		children,
		testid
	}: Props = $props();
</script>

<div
	class="bg-surface-1 p-4 rounded-xl shadow-sm border border-gray-200 dark:border-gray-700/50 ring-1 ring-black/5 dark:ring-white/5"
	data-testid={testid}
>
	<div class="flex items-center gap-1.5">
		{#if icon}
			{@const Icon = icon}
			<Icon size={14} class={iconClass} />
		{/if}
		<p class="text-xs font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">
			{label}
		</p>
	</div>
	{#if children}
		<div class="mt-1">{@render children()}</div>
	{:else}
		<p class="mt-1 text-xl font-bold tabular-nums {valueClass}">{value}</p>
	{/if}
</div>
