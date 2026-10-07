import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import DensityToggle from './DensityToggle.svelte';
import { density } from '$lib/stores/density.svelte';

describe('DensityToggle', () => {
	beforeEach(() => {
		localStorage.clear();
		// Reset the shared singleton so tests don't leak density into each other.
		density.set('comfortable');
	});

	it('should mark comfortable as pressed by default', () => {
		render(DensityToggle);
		expect(screen.getByTestId('density-toggle-comfortable')).toHaveAttribute('aria-pressed', 'true');
		expect(screen.getByTestId('density-toggle-compact')).toHaveAttribute('aria-pressed', 'false');
	});

	it('should switch to compact when the compact button is clicked', async () => {
		render(DensityToggle);

		await fireEvent.click(screen.getByTestId('density-toggle-compact'));

		expect(density.current).toBe('compact');
		expect(screen.getByTestId('density-toggle-compact')).toHaveAttribute('aria-pressed', 'true');
		expect(screen.getByTestId('density-toggle-comfortable')).toHaveAttribute('aria-pressed', 'false');
	});

	it('should switch back to comfortable when the comfortable button is clicked', async () => {
		density.set('compact');
		render(DensityToggle);

		await fireEvent.click(screen.getByTestId('density-toggle-comfortable'));

		expect(density.current).toBe('comfortable');
	});
});
