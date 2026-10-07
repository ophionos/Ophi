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

	function persist() {
		saveState({ dismissed, completed, currentStep });
	}

	function init() {
		const state = loadState();
		dismissed = state.dismissed;
		completed = state.completed;
		currentStep = state.currentStep;
	}

	function nextStep() {
		if (currentStep < TOTAL_STEPS - 1) {
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
			return TOTAL_STEPS;
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
