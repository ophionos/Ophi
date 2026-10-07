<script lang="ts">
	import { ChevronLeft, ChevronRight, X } from 'lucide-svelte';

	interface Props {
		targetSelector: string;
		title: string;
		description: string;
		step: number;
		totalSteps: number;
		position?: 'top' | 'bottom' | 'left' | 'right';
		onNext: () => void;
		onPrev: () => void;
		onDismiss: () => void;
	}

	let {
		targetSelector,
		title,
		description,
		step,
		totalSteps,
		position = 'bottom',
		onNext,
		onPrev,
		onDismiss
	}: Props = $props();

	let tooltipStyle = $state('');

	const isFirst = $derived(step === 0);
	const isLast = $derived(step === totalSteps - 1);

	$effect(() => {
		// Re-run when targetSelector changes
		const target = document.querySelector(targetSelector);
		if (!target) {
			tooltipStyle = 'top: 50%; left: 50%; transform: translate(-50%, -50%)';
			return;
		}

		const rect = target.getBoundingClientRect();
		const padding = 8;

		switch (position) {
			case 'bottom':
				tooltipStyle = `top: ${rect.bottom + padding + window.scrollY}px; left: ${rect.left + rect.width / 2 + window.scrollX}px; transform: translateX(-50%)`;
				break;
			case 'top':
				tooltipStyle = `bottom: ${window.innerHeight - rect.top + padding}px; left: ${rect.left + rect.width / 2 + window.scrollX}px; transform: translateX(-50%)`;
				break;
			case 'left':
				tooltipStyle = `top: ${rect.top + rect.height / 2 + window.scrollY}px; right: ${window.innerWidth - rect.left + padding}px; transform: translateY(-50%)`;
				break;
			case 'right':
				tooltipStyle = `top: ${rect.top + rect.height / 2 + window.scrollY}px; left: ${rect.right + padding + window.scrollX}px; transform: translateY(-50%)`;
				break;
		}
	});
</script>

<!-- Spotlight overlay -->
<div class="fixed inset-0 z-[90] pointer-events-none" aria-hidden="true">
	<div class="absolute inset-0 bg-black/40"></div>
</div>

<!-- Tooltip -->
<div
	class="fixed z-[91] w-72 bg-white dark:bg-gray-800 rounded-lg shadow-xl border border-gray-200 dark:border-gray-700 p-4"
	style={tooltipStyle}
	role="dialog"
	aria-modal="false"
	aria-label="Onboarding step {step + 1} of {totalSteps}"
	data-testid="onboarding-tooltip"
>
	<div class="flex items-start justify-between mb-2">
		<h3 class="text-sm font-semibold text-gray-900 dark:text-white" data-testid="onboarding-title">{title}</h3>
		<button
			onclick={onDismiss}
			class="p-0.5 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 rounded"
			aria-label="Skip tour"
		>
			<X size={14} />
		</button>
	</div>

	<p class="text-xs text-gray-600 dark:text-gray-400 mb-3" data-testid="onboarding-description">{description}</p>

	<div class="flex items-center justify-between">
		<span class="text-xs text-gray-400" data-testid="onboarding-counter">{step + 1} / {totalSteps}</span>

		<div class="flex items-center gap-2">
			{#if !isFirst}
				<button
					onclick={onPrev}
					class="flex items-center gap-1 px-2 py-1 text-xs text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white rounded"
					data-testid="onboarding-prev"
				>
					<ChevronLeft size={12} />
					Back
				</button>
			{/if}
			<button
				onclick={isLast ? onDismiss : onNext}
				class="flex items-center gap-1 px-3 py-1 text-xs font-medium text-white bg-brand hover:bg-brand-hover rounded"
				data-testid="onboarding-next"
			>
				{isLast ? 'Finish' : 'Next'}
				{#if !isLast}
					<ChevronRight size={12} />
				{/if}
			</button>
		</div>
	</div>
</div>
