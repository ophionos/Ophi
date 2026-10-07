import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ViewModeToggle from './ViewModeToggle.svelte';

describe('ViewModeToggle', () => {
	it('marks the active mode with aria-pressed', () => {
		render(ViewModeToggle, { props: { mode: 'list', onModeChange: vi.fn() } });
		expect(screen.getByTestId('view-toggle-list')).toHaveAttribute('aria-pressed', 'true');
		expect(screen.getByTestId('view-toggle-grid')).toHaveAttribute('aria-pressed', 'false');
		expect(screen.getByTestId('view-toggle-feed')).toHaveAttribute('aria-pressed', 'false');
	});

	it('invokes onModeChange when a new mode is clicked', async () => {
		const onModeChange = vi.fn();
		render(ViewModeToggle, { props: { mode: 'grid', onModeChange } });
		await fireEvent.click(screen.getByTestId('view-toggle-feed'));
		expect(onModeChange).toHaveBeenCalledWith('feed');
	});
});
