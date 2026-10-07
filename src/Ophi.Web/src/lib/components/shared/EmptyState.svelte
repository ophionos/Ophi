<script lang="ts">
	import type { ComponentType, SvelteComponent, Snippet } from 'svelte';
	import type { IconProps } from 'lucide-svelte';

	// lucide-svelte icons are legacy class components — same typing as SectionCard.
	type IconComponent = ComponentType<SvelteComponent<IconProps>>;

	interface Props {
		icon: IconComponent;
		/** Small secondary icon overlapping the main one (the dashboard's "price drop" accent). */
		badgeIcon?: IconComponent;
		title: string;
		description?: string;
		/** `lg` is the dashboard's first-run hero; `md` suits an empty section inside a page. */
		size?: 'md' | 'lg';
		/** False when already inside a card (e.g. a SectionCard body) — avoids a card-in-card border. */
		framed?: boolean;
		testid?: string;
		/** Next action — a button or link. */
		children?: Snippet;
	}

	let {
		icon: Icon,
		badgeIcon: BadgeIcon,
		title,
		description,
		size = 'md',
		framed = true,
		testid,
		children
	}: Props = $props();

	const lg = $derived(size === 'lg');
</script>

<div
	class="text-center {lg ? 'py-20' : 'py-10 px-4'} {!framed
		? ''
		: lg
			? 'bg-white/50 dark:bg-gray-800/40 backdrop-blur-md rounded-2xl shadow-inner border border-gray-200 dark:border-white/5'
			: 'bg-surface-1 rounded-xl border border-gray-200 dark:border-white/5'}"
	data-testid={testid}
>
	<div class="flex justify-center {lg ? 'mb-6' : 'mb-4'}">
		<div class="relative">
			<div
				class="{lg
					? 'w-24 h-24'
					: 'w-14 h-14'} bg-brand-subtle dark:bg-brand-dark/30 rounded-full flex items-center justify-center"
			>
				<Icon size={lg ? 40 : 24} class="text-brand dark:text-brand-muted" />
			</div>
			{#if BadgeIcon}
				<div
					class="absolute -bottom-1 -right-1 w-10 h-10 bg-green-100 dark:bg-green-900/30 rounded-full flex items-center justify-center"
				>
					<BadgeIcon size={20} class="text-green-600 dark:text-green-400" />
				</div>
			{/if}
		</div>
	</div>

	<h3
		class="{lg ? 'text-xl' : 'text-base'} font-semibold text-gray-900 dark:text-white mb-1"
	>
		{title}
	</h3>
	{#if description}
		<p class="text-sm text-gray-500 dark:text-gray-400 max-w-sm mx-auto">{description}</p>
	{/if}

	{#if children}
		<div class={lg ? 'mt-6' : 'mt-4'}>
			{@render children()}
		</div>
	{/if}
</div>
