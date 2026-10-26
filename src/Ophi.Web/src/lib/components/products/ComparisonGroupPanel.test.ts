import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import ComparisonGroupPanel from './ComparisonGroupPanel.svelte';
import type { ComparisonGroup } from '$lib/api/client';

vi.mock('$app/paths', () => ({ resolve: (path: string) => path }));

const group = { id: 'g1', name: 'Wheels', productCount: 2 } as ComparisonGroup;

describe('ComparisonGroupPanel', () => {
	it('should centre the View Group link like the Remove button when in a group', () => {
		render(ComparisonGroupPanel, {
			props: { groups: [group], currentGroupId: 'g1', onAdd: vi.fn(), onRemove: vi.fn() }
		});

		// A <button> centres its label in the 44 px target by default; an <a> needs flex to match.
		const link = screen.getByRole('link', { name: 'View Group' });
		expect(link).toHaveAttribute('href', '/comparisons/g1');
		expect(link.className).toContain('min-h-11');
		expect(link.className).toContain('inline-flex');
		expect(link.className).toContain('items-center');
	});
});
