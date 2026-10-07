import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock $app/environment before importing the store
vi.mock('$app/environment', () => ({ browser: true }));

describe('viewMode store', () => {
	beforeEach(() => {
		localStorage.clear();
		// Reset module to get fresh singleton state between tests
		vi.resetModules();
	});

	it('should default to grid when no stored preference', async () => {
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('grid');
	});

	it('should restore list from localStorage', async () => {
		localStorage.setItem('dashboard-view-mode', 'list');
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('list');
	});

	it('should restore feed from localStorage', async () => {
		localStorage.setItem('dashboard-view-mode', 'feed');
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('feed');
	});

	it('should ignore an invalid stored value and fall back to grid', async () => {
		localStorage.setItem('dashboard-view-mode', 'mosaic');
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('grid');
	});

	it('should set and persist the mode', async () => {
		const { viewMode } = await import('./viewMode.svelte');
		viewMode.set('feed');
		expect(viewMode.current).toBe('feed');
		expect(localStorage.getItem('dashboard-view-mode')).toBe('feed');
	});

	describe('count-based default', () => {
		it('should switch an untouched dashboard to list at the threshold', async () => {
			const { viewMode, LIST_DEFAULT_THRESHOLD } = await import('./viewMode.svelte');
			viewMode.applyCountDefault(LIST_DEFAULT_THRESHOLD);
			expect(viewMode.current).toBe('list');
		});

		it('should stay on grid below the threshold', async () => {
			const { viewMode, LIST_DEFAULT_THRESHOLD } = await import('./viewMode.svelte');
			viewMode.applyCountDefault(LIST_DEFAULT_THRESHOLD - 1);
			expect(viewMode.current).toBe('grid');
		});

		it('should respect an explicitly chosen grid even past the threshold', async () => {
			// A stored value means the user used the toggle at some point; 'grid' is then a real
			// preference rather than the untouched default, so the count must not override it.
			localStorage.setItem('dashboard-view-mode', 'grid');
			const { viewMode } = await import('./viewMode.svelte');
			viewMode.applyCountDefault(500);
			expect(viewMode.current).toBe('grid');
		});

		it('should not override a choice made during the session', async () => {
			const { viewMode } = await import('./viewMode.svelte');
			viewMode.set('grid');
			viewMode.applyCountDefault(500);
			expect(viewMode.current).toBe('grid');
		});

		it('should not persist the count-based default', async () => {
			const { viewMode, LIST_DEFAULT_THRESHOLD } = await import('./viewMode.svelte');
			viewMode.applyCountDefault(LIST_DEFAULT_THRESHOLD);
			// Nothing was chosen, so deleting products should be able to take it back to grid.
			expect(localStorage.getItem('dashboard-view-mode')).toBeNull();
			viewMode.applyCountDefault(1);
			expect(viewMode.current).toBe('grid');
		});
	});
});
