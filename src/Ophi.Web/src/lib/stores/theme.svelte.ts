import { browser } from '$app/environment';

type Theme = 'light' | 'dark';

function getInitialTheme(): Theme {
	if (!browser) return 'light';

	const stored = localStorage.getItem('theme') as Theme | null;
	if (stored === 'light' || stored === 'dark') {
		return stored;
	}

	if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
		return 'dark';
	}

	return 'light';
}

function updateDocumentClass(t: Theme) {
	if (t === 'dark') {
		document.documentElement.classList.add('dark');
	} else {
		document.documentElement.classList.remove('dark');
	}
}

let current = $state<Theme>(getInitialTheme());

export const theme = {
	get current() {
		return current;
	},
	toggle() {
		const newTheme = current === 'light' ? 'dark' : 'light';
		if (browser) {
			localStorage.setItem('theme', newTheme);
			updateDocumentClass(newTheme);
		}
		current = newTheme;
	},
	set(value: Theme) {
		if (browser) {
			localStorage.setItem('theme', value);
			updateDocumentClass(value);
		}
		current = value;
	},
	init() {
		if (browser) {
			const value = getInitialTheme();
			updateDocumentClass(value);
			current = value;
		}
	}
};
