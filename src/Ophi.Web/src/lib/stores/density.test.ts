import { describe, it, expect, beforeEach, vi } from 'vitest';

// Mock $app/environment before importing the store
vi.mock('$app/environment', () => ({ browser: true }));

describe('density store', () => {
	beforeEach(() => {
		localStorage.clear();
		// Reset module to get fresh singleton state between tests
		vi.resetModules();
	});

	it('should default to comfortable when no stored preference', async () => {
		const { density } = await import('./density.svelte');
		expect(density.current).toBe('comfortable');
	});

	it('should restore compact from localStorage', async () => {
		localStorage.setItem('product-table-density', 'compact');
		const { density } = await import('./density.svelte');
		expect(density.current).toBe('compact');
	});

	it('should ignore an invalid stored value and fall back to comfortable', async () => {
		localStorage.setItem('product-table-density', 'cozy');
		const { density } = await import('./density.svelte');
		expect(density.current).toBe('comfortable');
	});

	it('should set and persist compact', async () => {
		const { density } = await import('./density.svelte');
		density.set('compact');
		expect(density.current).toBe('compact');
		expect(localStorage.getItem('product-table-density')).toBe('compact');
	});

	it('should toggle from comfortable to compact', async () => {
		const { density } = await import('./density.svelte');
		density.set('comfortable');

		density.toggle();

		expect(density.current).toBe('compact');
		expect(localStorage.getItem('product-table-density')).toBe('compact');
	});

	it('should toggle from compact to comfortable', async () => {
		const { density } = await import('./density.svelte');
		density.set('compact');

		density.toggle();

		expect(density.current).toBe('comfortable');
		expect(localStorage.getItem('product-table-density')).toBe('comfortable');
	});
});
