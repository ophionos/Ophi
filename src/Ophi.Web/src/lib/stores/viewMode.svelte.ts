import { browser } from '$app/environment';

export type ViewMode = 'grid' | 'list' | 'feed';

const STORAGE_KEY = 'dashboard-view-mode';

/**
 * Past this many products the grid stops being the denser fit and list becomes the better
 * default. Only applies to a dashboard whose view has never been chosen by hand.
 */
export const LIST_DEFAULT_THRESHOLD = 12;

/**
 * A stored value only exists once the user has used the view toggle, so its presence — not its
 * value — is what distinguishes a deliberate 'grid' from the untouched default.
 */
function readStoredChoice(): ViewMode | null {
	if (!browser) return null;

	const stored = localStorage.getItem(STORAGE_KEY);
	return stored === 'list' || stored === 'feed' || stored === 'grid' ? stored : null;
}

const storedChoice = readStoredChoice();

let explicitChoice = $state<ViewMode | null>(storedChoice);
let current = $state<ViewMode>(storedChoice ?? 'grid');

export const viewMode = {
	get current() {
		return current;
	},
	get hasExplicitChoice() {
		return explicitChoice !== null;
	},
	set(value: ViewMode) {
		if (browser) {
			localStorage.setItem(STORAGE_KEY, value);
		}
		explicitChoice = value;
		current = value;
	},
	/**
	 * Applies the count-based default. Deliberately does not persist: an untouched dashboard
	 * should follow the product count in both directions, and writing here would forge a
	 * preference the user never expressed. No-op once they have chosen a view themselves.
	 */
	applyCountDefault(productCount: number) {
		if (explicitChoice !== null) return;
		current = productCount >= LIST_DEFAULT_THRESHOLD ? 'list' : 'grid';
	}
};
