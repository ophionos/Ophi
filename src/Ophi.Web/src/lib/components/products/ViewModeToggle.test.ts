import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ViewModeToggle from './ViewModeToggle.svelte';

describe('ViewModeToggle', () => {
	it('marks the active mode with aria-pressed', () => {
		render(ViewModeToggle, { props: { mode: 'list', onModeChange: vi.fn() } });
		expect(screen.getByTestId('view-toggle-list')).toHaveAttribute('aria-pressed', 'true');
		expect(screen.getByTestId('view-toggle-grid')).toHaveAttribute('aria-pressed', 'false');
	});

	it('names the views Table and Gallery', () => {
		render(ViewModeToggle, { props: { mode: 'list', onModeChange: vi.fn() } });
		expect(screen.getByRole('button', { name: 'Table' })).toBeInTheDocument();
		expect(screen.getByRole('button', { name: 'Gallery' })).toBeInTheDocument();
	});

	it('invokes onModeChange when a new mode is clicked', async () => {
		const onModeChange = vi.fn();
		render(ViewModeToggle, { props: { mode: 'list', onModeChange } });
		await fireEvent.click(screen.getByTestId('view-toggle-grid'));
		expect(onModeChange).toHaveBeenCalledWith('grid');
	});
});
