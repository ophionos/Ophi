import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock $app/environment before importing the store
vi.mock('$app/environment', () => ({ browser: true }));

describe('viewMode store', () => {
	beforeEach(() => {
		localStorage.clear();
		// Reset module to get fresh singleton state between tests
		vi.resetModules();
	});

	it('should default to the table when no stored preference', async () => {
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('list');
	});

	it('should restore the gallery from localStorage', async () => {
		localStorage.setItem('dashboard-view-mode', 'grid');
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('grid');
	});

	it('should read the retired feed mode as the table', async () => {
		localStorage.setItem('dashboard-view-mode', 'feed');
		const { viewMode } = await import('./viewMode.svelte');
		expect(viewMode.current).toBe('list');
	});

	it('should persist a choice', async () => {
		const { viewMode } = await import('./viewMode.svelte');
		viewMode.set('grid');
		expect(viewMode.current).toBe('grid');
		expect(localStorage.getItem('dashboard-view-mode')).toBe('grid');
	});
});
