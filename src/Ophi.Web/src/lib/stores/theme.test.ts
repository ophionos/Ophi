import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock $app/environment before importing the store
vi.mock('$app/environment', () => ({ browser: true }));

describe('theme store', () => {
	beforeEach(() => {
		localStorage.clear();
		document.documentElement.classList.remove('dark');
		// Reset module to get fresh state
		vi.resetModules();
	});

	it('should default to light theme when no stored preference', async () => {
		const { theme } = await import('./theme.svelte');
		theme.init();
		expect(theme.current).toBe('light');
	});

	it('should restore dark theme from localStorage', async () => {
		localStorage.setItem('theme', 'dark');
		const { theme } = await import('./theme.svelte');
		theme.init();
		expect(theme.current).toBe('dark');
	});

	it('should toggle from light to dark', async () => {
		const { theme } = await import('./theme.svelte');
		theme.set('light');

		theme.toggle();

		expect(theme.current).toBe('dark');
		expect(localStorage.getItem('theme')).toBe('dark');
		expect(document.documentElement.classList.contains('dark')).toBe(true);
	});

	it('should toggle from dark to light', async () => {
		const { theme } = await import('./theme.svelte');
		theme.set('dark');

		theme.toggle();

		expect(theme.current).toBe('light');
		expect(localStorage.getItem('theme')).toBe('light');
		expect(document.documentElement.classList.contains('dark')).toBe(false);
	});

	it('should set theme directly', async () => {
		const { theme } = await import('./theme.svelte');

		theme.set('dark');

		expect(theme.current).toBe('dark');
		expect(localStorage.getItem('theme')).toBe('dark');
	});

	it('should add dark class when set to dark', async () => {
		const { theme } = await import('./theme.svelte');

		theme.set('dark');

		expect(document.documentElement.classList.contains('dark')).toBe(true);
	});

	it('should remove dark class when set to light', async () => {
		document.documentElement.classList.add('dark');
		const { theme } = await import('./theme.svelte');

		theme.set('light');

		expect(document.documentElement.classList.contains('dark')).toBe(false);
	});
});
