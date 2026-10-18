<script lang="ts" module>
	import type { ComponentType, SvelteComponent } from 'svelte';
	import type { IconProps } from 'lucide-svelte';

	export interface ActionMenuItem {
		label: string;
		// lucide-svelte icons are legacy class components — same typing as SectionCard.
		icon?: ComponentType<SvelteComponent<IconProps>>;
		/** A link item; opens in a new tab when `external` is set. */
		href?: string;
		external?: boolean;
		onSelect?: () => void;
		danger?: boolean;
		testid?: string;
	}
</script>

<script lang="ts">
	import { tick } from 'svelte';
	import { Ellipsis } from 'lucide-svelte';

	interface Props {
		/** Accessible name of the trigger; include what the menu acts on ("Actions for Desk Lamp"). */
		label: string;
		items: ActionMenuItem[];
		testid?: string;
	}

	let { label, items, testid = 'action-menu' }: Props = $props();

	const ITEM_HEIGHT = 44;

	let open = $state(false);
	let trigger = $state<HTMLButtonElement>();
	let menu = $state<HTMLDivElement>();
	let menuStyle = $state('');

	// Fixed, not absolute: the menu sits inside table wrappers that clip overflow, so an absolute
	// menu on the last rows would be cut off. It opens upwards when there is no room below.
	async function openMenu() {
		if (!trigger) return;
		const rect = trigger.getBoundingClientRect();
		const height = items.length * ITEM_HEIGHT + 8;
		const right = window.innerWidth - rect.right;
		menuStyle =
			window.innerHeight - rect.bottom < height + 8 && rect.top > height + 8
				? `bottom: ${window.innerHeight - rect.top + 4}px; right: ${right}px`
				: `top: ${rect.bottom + 4}px; right: ${right}px`;
		open = true;
		await tick();
		menuItems()[0]?.focus();
	}

	function close(returnFocus: boolean) {
		open = false;
		if (returnFocus) trigger?.focus();
	}

	function menuItems(): HTMLElement[] {
		return menu ? Array.from(menu.querySelectorAll<HTMLElement>('[role="menuitem"]')) : [];
	}

	function select(item: ActionMenuItem) {
		close(true);
		item.onSelect?.();
	}

	function onMenuKeydown(event: KeyboardEvent) {
		const elements = menuItems();
		const index = elements.indexOf(document.activeElement as HTMLElement);
		if (event.key === 'Escape') {
			event.preventDefault();
			close(true);
		} else if (event.key === 'ArrowDown') {
			event.preventDefault();
			elements[(index + 1) % elements.length]?.focus();
		} else if (event.key === 'ArrowUp') {
			event.preventDefault();
			elements[(index - 1 + elements.length) % elements.length]?.focus();
		} else if (event.key === 'Tab') {
			close(false);
		}
	}

	// A fixed menu would float away from its row on scroll, so any scroll or resize closes it.
	$effect(() => {
		if (!open) return;
		const onPointerDown = (event: PointerEvent) => {
			const target = event.target as Node;
			if (!menu?.contains(target) && !trigger?.contains(target)) close(false);
		};
		const onScroll = () => close(false);
		document.addEventListener('pointerdown', onPointerDown);
		window.addEventListener('scroll', onScroll, true);
		window.addEventListener('resize', onScroll);
		return () => {
			document.removeEventListener('pointerdown', onPointerDown);
			window.removeEventListener('scroll', onScroll, true);
			window.removeEventListener('resize', onScroll);
		};
	});
</script>

<button
	bind:this={trigger}
	type="button"
	onclick={() => (open ? close(true) : openMenu())}
	class="inline-flex min-h-11 min-w-11 items-center justify-center rounded-lg text-gray-500 hover:bg-surface-2 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
	aria-label={label}
	aria-haspopup="menu"
	aria-expanded={open}
	data-testid={testid}
>
	<Ellipsis size={18} aria-hidden="true" />
</button>

{#if open}
	<div
		bind:this={menu}
		role="menu"
		tabindex="-1"
		aria-label={label}
		class="fixed z-50 min-w-44 rounded-lg border border-gray-200 bg-surface-1 py-1 shadow-lg dark:border-gray-700"
		style={menuStyle}
		onkeydown={onMenuKeydown}
		data-testid="{testid}-items"
	>
		{#each items as item (item.label)}
			{@const Icon = item.icon}
			{@const itemClass = `flex min-h-11 w-full items-center gap-2 px-3 text-left text-sm hover:bg-surface-2 focus:bg-surface-2 focus:outline-none ${item.danger ? 'text-danger' : 'text-gray-700 dark:text-gray-200'}`}
			{#if item.href}
				<!-- eslint-disable svelte/no-navigation-without-resolve -- callers pass resolved or external hrefs -->
				<a
					role="menuitem"
					tabindex="-1"
					href={item.href}
					target={item.external ? '_blank' : undefined}
					rel={item.external ? 'noopener noreferrer external' : undefined}
					class={itemClass}
					onclick={() => select(item)}
					data-testid={item.testid}
				>
					{#if Icon}<Icon size={15} aria-hidden="true" />{/if}
					{item.label}
				</a>
				<!-- eslint-enable svelte/no-navigation-without-resolve -->
			{:else}
				<button
					role="menuitem"
					tabindex="-1"
					type="button"
					class={itemClass}
					onclick={() => select(item)}
					data-testid={item.testid}
				>
					{#if Icon}<Icon size={15} aria-hidden="true" />{/if}
					{item.label}
				</button>
			{/if}
		{/each}
	</div>
{/if}
