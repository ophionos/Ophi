<script lang="ts">
	import { X } from 'lucide-svelte';
	import { focusTrap } from '$lib/actions/focusTrap';
	import type { Snippet } from 'svelte';

	type Size = 'sm' | 'md' | 'lg' | 'xl' | '2xl' | '3xl';

	interface Props {
		isOpen: boolean;
		title: string;
		size?: Size;
		position?: 'top' | 'center';
		closeOnBackdrop?: boolean;
		closeOnEscape?: boolean;
		closeDisabled?: boolean;
		titleId?: string;
		onClose: () => void;
		children: Snippet;
	}

	let {
		isOpen,
		title,
		size = 'md',
		position = 'top',
		closeOnBackdrop = true,
		closeOnEscape = true,
		closeDisabled = false,
		titleId,
		onClose,
		children
	}: Props = $props();

	const sizeClass: Record<Size, string> = {
		sm: 'max-w-sm',
		md: 'max-w-md',
		lg: 'max-w-lg',
		xl: 'max-w-xl',
		'2xl': 'max-w-2xl',
		'3xl': 'max-w-3xl'
	};

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && closeOnEscape) {
			onClose();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if isOpen}
	<div class="fixed inset-0 z-50 overflow-y-auto">
		<div
			class="flex min-h-full justify-center p-4 {position === 'center'
				? 'items-center'
				: 'items-start pt-16'}"
		>
			<div
				class="fixed inset-0 bg-black/50 transition-opacity"
				onclick={() => closeOnBackdrop && onClose()}
				role="presentation"
			></div>

			<div
				class="relative bg-white dark:bg-gray-800 rounded-lg shadow-xl {sizeClass[size]} w-full max-h-[85vh] overflow-hidden flex flex-col"
				role="dialog"
				aria-modal="true"
				aria-labelledby={titleId}
				aria-label={titleId ? undefined : title}
				use:focusTrap
			>
				<div
					class="flex items-center justify-between px-6 py-4 border-b border-gray-200 dark:border-gray-700"
				>
					<h2
						id={titleId}
						class="text-lg font-semibold text-gray-900 dark:text-white"
					>
						{title}
					</h2>
					<button
						onclick={onClose}
						disabled={closeDisabled}
						class="p-2 min-w-10 min-h-10 flex items-center justify-center text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-full disabled:opacity-50 disabled:cursor-not-allowed"
						aria-label="Close"
					>
						<X size={20} />
					</button>
				</div>

				{@render children()}
			</div>
		</div>
	</div>
{/if}
