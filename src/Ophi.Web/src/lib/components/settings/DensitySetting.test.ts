import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import DensitySetting from './DensitySetting.svelte';
import { density } from '$lib/stores/density.svelte';

describe('DensitySetting', () => {
	beforeEach(() => {
		density.set('comfortable');
	});

	it('should explain that the density applies to this device', () => {
		render(DensitySetting);

		expect(screen.getByText('Table density')).toBeInTheDocument();
		expect(screen.getByText('The watchlist table on this device.')).toBeInTheDocument();
	});

	it('should check the current density when rendered', () => {
		density.set('compact');
		render(DensitySetting);

		expect(screen.getByRole('radio', { name: 'Compact' })).toBeChecked();
		expect(screen.getByRole('radio', { name: 'Comfortable' })).not.toBeChecked();
	});

	it('should set the density store to compact when Compact is chosen', async () => {
		render(DensitySetting);

		await fireEvent.click(screen.getByRole('radio', { name: 'Compact' }));

		expect(density.current).toBe('compact');
	});

	it('should set the density store to comfortable when Comfortable is chosen', async () => {
		density.set('compact');
		render(DensitySetting);

		await fireEvent.click(screen.getByRole('radio', { name: 'Comfortable' }));

		expect(density.current).toBe('comfortable');
	});
});
