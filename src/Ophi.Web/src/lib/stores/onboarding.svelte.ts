const STORAGE_KEY = 'ophi-onboarding';
const TOTAL_STEPS = 5;

interface OnboardingState {
	dismissed: boolean;
	completed: boolean;
	currentStep: number;
}

function loadState(): OnboardingState {
	if (typeof window === 'undefined') return { dismissed: false, completed: false, currentStep: 0 };
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (raw) return JSON.parse(raw);
	} catch {
		// ignore corrupt data
	}
	return { dismissed: false, completed: false, currentStep: 0 };
}

function saveState(state: OnboardingState) {
	if (typeof window === 'undefined') return;
	try {
		localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
	} catch {
		// ignore quota errors
	}
}

function createOnboardingStore() {
	let dismissed = $state(false);
	let completed = $state(false);
	let currentStep = $state(0);
	let totalSteps = $state(TOTAL_STEPS);

	function persist() {
		saveState({ dismissed, completed, currentStep });
	}

	/** `steps` is the number of steps the page can show; a saved step past it moves to the last one. */
	function init(steps = TOTAL_STEPS) {
		const state = loadState();
		totalSteps = steps;
		dismissed = state.dismissed;
		completed = state.completed;
		currentStep = Math.max(0, Math.min(state.currentStep, steps - 1));
	}

	function nextStep() {
		if (currentStep < totalSteps - 1) {
			currentStep++;
			persist();
		}
	}

	function prevStep() {
		if (currentStep > 0) {
			currentStep--;
			persist();
		}
	}

	function dismiss() {
		dismissed = true;
		persist();
	}

	function complete() {
		completed = true;
		dismissed = true;
		persist();
	}

	function reset() {
		dismissed = false;
		completed = false;
		currentStep = 0;
		persist();
	}

	return {
		get shouldShow() {
			return !dismissed && !completed;
		},
		get currentStep() {
			return currentStep;
		},
		get completed() {
			return completed;
		},
		get totalSteps() {
			return totalSteps;
		},
		init,
		nextStep,
		prevStep,
		dismiss,
		complete,
		reset
	};
}

export const onboarding = createOnboardingStore();
