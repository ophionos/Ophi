import { describe, it, expect, beforeEach, vi } from 'vitest';

// Must import fresh each test to reset $state
let onboarding: typeof import('./onboarding.svelte').onboarding;

beforeEach(async () => {
	localStorage.clear();
	// Re-import to reset module-level $state
	vi.resetModules();
	const mod = await import('./onboarding.svelte');
	onboarding = mod.onboarding;
	onboarding.init();
});

describe('onboarding store', () => {
	it('should show by default', () => {
		expect(onboarding.shouldShow).toBe(true);
		expect(onboarding.currentStep).toBe(0);
		expect(onboarding.completed).toBe(false);
	});

	it('should advance step with nextStep()', () => {
		onboarding.nextStep();
		expect(onboarding.currentStep).toBe(1);
	});

	it('should go back with prevStep()', () => {
		onboarding.nextStep();
		onboarding.nextStep();
		onboarding.prevStep();
		expect(onboarding.currentStep).toBe(1);
	});

	it('should not go below step 0', () => {
		onboarding.prevStep();
		expect(onboarding.currentStep).toBe(0);
	});

	it('should not go above last step', () => {
		for (let i = 0; i < 10; i++) onboarding.nextStep();
		expect(onboarding.currentStep).toBe(onboarding.totalSteps - 1);
	});

	it('should hide when dismissed', () => {
		onboarding.dismiss();
		expect(onboarding.shouldShow).toBe(false);
	});

	it('should hide and mark completed when complete() is called', () => {
		onboarding.complete();
		expect(onboarding.shouldShow).toBe(false);
		expect(onboarding.completed).toBe(true);
	});

	it('should persist state to localStorage', () => {
		onboarding.nextStep();
		onboarding.nextStep();

		const stored = JSON.parse(localStorage.getItem('ophi-onboarding')!);
		expect(stored.currentStep).toBe(2);
		expect(stored.dismissed).toBe(false);
	});

	it('should restore state from localStorage on init()', async () => {
		localStorage.setItem(
			'ophi-onboarding',
			JSON.stringify({ dismissed: false, completed: false, currentStep: 3 })
		);

		vi.resetModules();
		const mod = await import('./onboarding.svelte');
		mod.onboarding.init();

		expect(mod.onboarding.currentStep).toBe(3);
		expect(mod.onboarding.shouldShow).toBe(true);
	});

	it('should reset to default state', () => {
		onboarding.nextStep();
		onboarding.dismiss();
		onboarding.reset();

		expect(onboarding.shouldShow).toBe(true);
		expect(onboarding.currentStep).toBe(0);
		expect(onboarding.completed).toBe(false);
	});

	it('should expose totalSteps', () => {
		expect(onboarding.totalSteps).toBe(5);
	});
});
