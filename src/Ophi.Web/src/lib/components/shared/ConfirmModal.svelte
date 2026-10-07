<script lang="ts">
	import { LoaderCircle, TriangleAlert } from 'lucide-svelte';
	import { focusTrap } from '$lib/actions/focusTrap';

	interface Props {
		isOpen: boolean;
		title: string;
		message: string;
		confirmText?: string;
		cancelText?: string;
		onConfirm: () => void;
		onCancel: () => void;
		loading?: boolean;
	}

	let {
		isOpen,
		title,
		message,
		confirmText = 'Delete',
		cancelText = 'Cancel',
		onConfirm,
		onCancel,
		loading = false
	}: Props = $props();

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' && !loading) {
			onCancel();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if isOpen}
	<div class="fixed inset-0 z-50 overflow-y-auto">
		<div class="flex min-h-full items-center justify-center p-4">
			<!-- Backdrop -->
			<div
				class="fixed inset-0 bg-black/50 transition-opacity"
				onclick={() => !loading && onCancel()}
				role="presentation"
			></div>

			<!-- Modal -->
			<div class="relative bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full p-6" role="dialog" aria-modal="true" aria-labelledby="confirm-modal-title" aria-describedby="confirm-modal-desc" use:focusTrap>
				<div class="flex items-start gap-4">
					<div
						class="shrink-0 w-10 h-10 rounded-full bg-red-100 dark:bg-red-900 flex items-center justify-center"
					>
						<TriangleAlert size={20} class="text-red-600 dark:text-red-400" />
					</div>
					<div class="flex-1">
						<h3 id="confirm-modal-title" class="text-lg font-semibold text-gray-900 dark:text-white">{title}</h3>
						<p id="confirm-modal-desc" class="mt-2 text-sm text-gray-600 dark:text-gray-300">{message}</p>
					</div>
				</div>

				<div class="mt-6 flex justify-end gap-3">
					<button
						type="button"
						onclick={onCancel}
						disabled={loading}
						class="px-4 py-2 min-h-10 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
					>
						{cancelText}
					</button>
					<button
						type="button"
						onclick={onConfirm}
						disabled={loading}
						class="px-4 py-2 min-h-10 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 disabled:opacity-50 flex items-center gap-2"
					>
						{#if loading}
							<LoaderCircle size={16} class="animate-spin" />
						{/if}
						{confirmText}
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}
