import { browser } from '$app/environment';

/** 'list' is the table, 'grid' the gallery. The stored values predate those names. */
export type ViewMode = 'grid' | 'list';

const STORAGE_KEY = 'dashboard-view-mode';

/**
 * The table is the default. The old activity-feed mode is now a side rail next to either view,
 * so a stored 'feed' (or anything unknown) reads as the table.
 */
function readStoredChoice(): ViewMode {
	if (!browser) return 'list';

	try {
		return localStorage.getItem(STORAGE_KEY) === 'grid' ? 'grid' : 'list';
	} catch {
		return 'list';
	}
}

let current = $state<ViewMode>(readStoredChoice());

export const viewMode = {
	get current() {
		return current;
	},
	set(value: ViewMode) {
		if (browser) {
			try {
				localStorage.setItem(STORAGE_KEY, value);
			} catch {
				// A per-viewer convenience: the view still switches without storage.
			}
		}
		current = value;
	}
};
