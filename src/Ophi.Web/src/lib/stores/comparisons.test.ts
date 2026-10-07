import { describe, it, expect, beforeEach } from 'vitest';
import { comparisons } from './comparisons.svelte';
import type { ComparisonGroup } from '$lib/api/client';

function mockGroup(overrides: Partial<ComparisonGroup> = {}): ComparisonGroup {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		name: overrides.name ?? 'Test Group',
		description: null,
		products: [],
		...overrides
	} as ComparisonGroup;
}

describe('comparisons store', () => {
	beforeEach(() => {
		comparisons.clear();
	});

	it('should start with empty items', () => {
		expect(comparisons.items).toEqual([]);
	});

	it('should set comparison groups', () => {
		comparisons.set([mockGroup({ name: 'A' }), mockGroup({ name: 'B' })]);
		expect(comparisons.items).toHaveLength(2);
	});

	it('should add a group', () => {
		comparisons.set([mockGroup({ name: 'Existing' })]);
		comparisons.add(mockGroup({ name: 'New' }));

		expect(comparisons.items).toHaveLength(2);
		expect(comparisons.items[1].name).toBe('New');
	});

	it('should update a group by id', () => {
		const g = mockGroup({ id: 'g1', name: 'Old' });
		comparisons.set([g]);

		comparisons.update('g1', mockGroup({ id: 'g1', name: 'Updated' }));

		expect(comparisons.items[0].name).toBe('Updated');
	});

	it('should remove a group by id', () => {
		const g1 = mockGroup({ id: 'keep' });
		const g2 = mockGroup({ id: 'remove' });
		comparisons.set([g1, g2]);

		comparisons.remove('remove');

		expect(comparisons.items).toHaveLength(1);
		expect(comparisons.items[0].id).toBe('keep');
	});

	it('should clear all groups', () => {
		comparisons.set([mockGroup(), mockGroup()]);
		comparisons.clear();
		expect(comparisons.items).toEqual([]);
	});
});
