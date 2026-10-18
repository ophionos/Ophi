<script lang="ts">
	import { onMount } from 'svelte';
	import { onboarding } from '$lib/stores/onboarding.svelte';
	import OnboardingTooltip from './OnboardingTooltip.svelte';

	const steps = [
		{
			targetSelector: '[data-onboarding="add-product"]',
			title: 'Add a product',
			description: 'Paste a product URL here to start tracking its price. Any store works, with tuned extraction for Amazon and eBay.',
			position: 'bottom' as const
		},
		{
			targetSelector: '[data-onboarding="view-toggles"]',
			title: 'Switch views',
			description: 'Switch between the table and the gallery. Recent price changes stay in the side rail.',
			position: 'bottom' as const
		},
		{
			targetSelector: '[data-onboarding="filter-bar"]',
			title: 'Filter & sort',
			description: 'Narrow the list by text, status, tag or store, and change the sort. Each filter in effect shows as a chip you can remove.',
			position: 'bottom' as const
		},
		{
			targetSelector: '[data-notification-bell]',
			title: 'Stay notified',
			description: 'Check your notifications here. Set up alerts on products to get notified when prices drop.',
			position: 'bottom' as const
		},
		{
			targetSelector: '[data-command-palette-trigger]',
			title: 'Quick navigation',
			description: 'Press Ctrl+K (or Cmd+K on Mac) to open the command palette. Search products, navigate pages, and toggle settings instantly.',
			position: 'bottom' as const
		}
	];

	onMount(() => {
		onboarding.init();
	});
</script>

{#if onboarding.shouldShow}
	{@const currentStepData = steps[onboarding.currentStep]}
	<OnboardingTooltip
		targetSelector={currentStepData.targetSelector}
		title={currentStepData.title}
		description={currentStepData.description}
		step={onboarding.currentStep}
		totalSteps={onboarding.totalSteps}
		position={currentStepData.position}
		onNext={() => onboarding.nextStep()}
		onPrev={() => onboarding.prevStep()}
		onDismiss={() => {
			if (onboarding.currentStep === onboarding.totalSteps - 1) {
				onboarding.complete();
			} else {
				onboarding.dismiss();
			}
		}}
	/>
{/if}
