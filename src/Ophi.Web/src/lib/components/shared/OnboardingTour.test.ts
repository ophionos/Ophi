import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import OnboardingTour from './OnboardingTour.svelte';

vi.mock('$lib/stores/onboarding.svelte', () => {
	let shouldShow = true;
	let currentStep = 0;
	const totalSteps = 5;
	let completed = false;

	return {
		onboarding: {
			get shouldShow() { return shouldShow; },
			get currentStep() { return currentStep; },
			get totalSteps() { return totalSteps; },
			get completed() { return completed; },
			init: vi.fn(),
			nextStep: vi.fn(() => { if (currentStep < totalSteps - 1) currentStep++; }),
			prevStep: vi.fn(() => { if (currentStep > 0) currentStep--; }),
			dismiss: vi.fn(() => { shouldShow = false; }),
			complete: vi.fn(() => { shouldShow = false; completed = true; }),
			reset: vi.fn(() => { shouldShow = true; currentStep = 0; completed = false; }),
			// For test reset
			_reset() { shouldShow = true; currentStep = 0; completed = false; }
		}
	};
});

import { onboarding } from '$lib/stores/onboarding.svelte';

beforeEach(() => {
	vi.clearAllMocks();
	(onboarding as unknown as { _reset(): void })._reset();
});

describe('OnboardingTour', () => {
	it('should render current step tooltip', () => {
		render(OnboardingTour);

		expect(screen.getByTestId('onboarding-title')).toHaveTextContent('Add a product');
		expect(screen.getByTestId('onboarding-counter')).toHaveTextContent('1 / 5');
	});

	it('should not name stores that have no tuned adapter', () => {
		// CodeStoreConfigProvider registers AmazonConfig and EbayConfig only; every other domain
		// scrapes through the generic selector fallback. Naming Walmart alongside Amazon and eBay
		// promises tuned extraction that does not exist, and the failure mode when the generic
		// selectors miss is a silently wrong price rather than a visible error. See issue #131.
		render(OnboardingTour);

		const description = screen.getByTestId('onboarding-description');
		expect(description).not.toHaveTextContent(/walmart/i);
		// The generic fallback is real coverage, so the copy should still promise breadth.
		expect(description).toHaveTextContent(/any store/i);
	});

	it('should call nextStep when Next is clicked', async () => {
		render(OnboardingTour);

		await fireEvent.click(screen.getByTestId('onboarding-next'));
		expect(onboarding.nextStep).toHaveBeenCalled();
	});

	it('should call dismiss when Skip is clicked', async () => {
		render(OnboardingTour);

		await fireEvent.click(screen.getByLabelText('Skip tour'));
		expect(onboarding.dismiss).toHaveBeenCalled();
	});

	it('should call init on mount', () => {
		render(OnboardingTour);
		expect(onboarding.init).toHaveBeenCalled();
	});

	it('should not render when shouldShow is false', () => {
		onboarding.dismiss();
		render(OnboardingTour);

		expect(screen.queryByTestId('onboarding-tooltip')).not.toBeInTheDocument();
	});
});
