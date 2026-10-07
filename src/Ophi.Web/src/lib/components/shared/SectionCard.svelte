<script lang="ts">
	import type { ComponentType, SvelteComponent, Snippet } from 'svelte';
	import { ChevronDown } from 'lucide-svelte';
	import type { IconProps } from 'lucide-svelte';

	// lucide-svelte icons are legacy (SvelteComponentTyped) class components, so type the prop
	// with the legacy ComponentType wrapper rather than the Svelte 5 `Component` function type.
	type IconComponent = ComponentType<SvelteComponent<IconProps>>;

	interface Props {
		/** Section heading text. */
		title: string;
		/** Optional secondary line rendered under the title. */
		subtitle?: string;
		/** Optional lucide icon component rendered before the title. */
		icon?: IconComponent;
		/** Optional action rendered on the right of the header (e.g. a button). Hidden when collapsible. */
		action?: Snippet;
		/** When true the header becomes a toggle and the body collapses. */
		collapsible?: boolean;
		/** Open state for a collapsible section. The card toggles itself locally; a later prop change resets it. */
		open?: boolean;
		/** Called with the new open state whenever a collapsible header is toggled (e.g. for lazy loading). */
		onToggle?: (open: boolean) => void;
		/** Visual accent. `danger` uses a red border + red icon (e.g. Danger Zone). */
		accent?: 'default' | 'danger';
		/** Tailwind classes for the body wrapper. Defaults to `p-4`. */
		bodyClass?: string;
		titleId?: string;
		testid?: string;
		children: Snippet;
	}

	let {
		title,
		subtitle,
		icon,
		action,
		collapsible = false,
		open = true,
		onToggle,
		accent = 'default',
		bodyClass = 'p-4',
		titleId,
		testid,
		children
	}: Props = $props();

	// Writable derived: starts from the prop (and resets if it changes), while toggle() can reassign it locally.
	let isOpen = $derived(open);

	function toggle() {
		isOpen = !isOpen;
		onToggle?.(isOpen);
	}

	const cardBorder = $derived(
		accent === 'danger'
			? 'border-red-200 dark:border-red-900/50'
			: 'border-gray-200 dark:border-gray-700/50 ring-1 ring-black/5 dark:ring-white/5'
	);

	const iconClass = $derived(
		accent === 'danger' ? 'text-red-500 dark:text-red-400' : 'text-gray-400 dark:text-gray-500'
	);
</script>

<div class="bg-surface-1 rounded-2xl shadow-sm border {cardBorder} mb-6" data-testid={testid}>
	{#if collapsible}
		<button
			type="button"
			onclick={toggle}
			class="w-full flex items-center justify-between p-4 text-left"
			aria-expanded={isOpen}
		>
			<div class="min-w-0">
				<h2
					id={titleId}
					class="text-lg font-semibold text-gray-900 dark:text-white flex items-center gap-2"
				>
					{#if icon}
						{@const Icon = icon}
						<Icon size={20} class={iconClass} />
					{/if}
					{title}
				</h2>
				{#if subtitle}
					<p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">{subtitle}</p>
				{/if}
			</div>
			<ChevronDown
				size={18}
				class="text-gray-400 transition-transform {isOpen ? 'rotate-180' : ''}"
			/>
		</button>
		{#if isOpen}
			<div
				class="{bodyClass} border-t {accent === 'danger'
					? 'border-red-200 dark:border-red-900/50'
					: 'border-gray-200 dark:border-gray-700'}"
			>
				{@render children()}
			</div>
		{/if}
	{:else}
		<div
			class="flex flex-wrap items-center justify-between gap-3 p-4 border-b border-gray-200 dark:border-gray-700"
		>
			<div class="min-w-0">
				<h2
					id={titleId}
					class="text-lg font-semibold text-gray-900 dark:text-white flex items-center gap-2"
				>
					{#if icon}
						{@const Icon = icon}
						<Icon size={20} class={iconClass} />
					{/if}
					{title}
				</h2>
				{#if subtitle}
					<p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">{subtitle}</p>
				{/if}
			</div>
			{#if action}
				{@render action()}
			{/if}
		</div>
		<div class={bodyClass}>
			{@render children()}
		</div>
	{/if}
</div>
