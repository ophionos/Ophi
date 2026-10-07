import { browser } from '$app/environment';

export type Density = 'comfortable' | 'compact';

const STORAGE_KEY = 'product-table-density';

function getInitialDensity(): Density {
	if (!browser) return 'comfortable';

	const stored = localStorage.getItem(STORAGE_KEY);
	return stored === 'compact' ? 'compact' : 'comfortable';
}

let current = $state<Density>(getInitialDensity());

export const density = {
	get current() {
		return current;
	},
	set(value: Density) {
		if (browser) {
			localStorage.setItem(STORAGE_KEY, value);
		}
		current = value;
	},
	toggle() {
		this.set(current === 'comfortable' ? 'compact' : 'comfortable');
	}
};
