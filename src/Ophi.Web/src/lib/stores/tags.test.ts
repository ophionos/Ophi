import { describe, it, expect, beforeEach } from 'vitest';
import { tags } from './tags.svelte';
import type { Tag } from '$lib/api/client';

function mockTag(overrides: Partial<Tag> = {}): Tag {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		name: overrides.name ?? 'Test Tag',
		color: overrides.color ?? '#ff0000',
		...overrides
	} as Tag;
}

describe('tags store', () => {
	beforeEach(() => {
		tags.set([]);
	});

	it('should start with empty items', () => {
		expect(tags.items).toEqual([]);
	});

	it('should set tags', () => {
		tags.set([mockTag({ name: 'A' }), mockTag({ name: 'B' })]);
		expect(tags.items).toHaveLength(2);
	});

	it('should add a tag', () => {
		tags.set([mockTag({ name: 'Existing' })]);
		tags.add(mockTag({ name: 'New' }));

		expect(tags.items).toHaveLength(2);
		expect(tags.items[1].name).toBe('New');
	});

	it('should remove a tag by id', () => {
		const t1 = mockTag({ id: 'keep' });
		const t2 = mockTag({ id: 'remove' });
		tags.set([t1, t2]);

		tags.remove('remove');

		expect(tags.items).toHaveLength(1);
		expect(tags.items[0].id).toBe('keep');
	});

	it('should update a tag by id', () => {
		const t = mockTag({ id: 't1', name: 'Old' });
		tags.set([t]);

		tags.update('t1', { name: 'Updated' });

		expect(tags.items[0].name).toBe('Updated');
	});

	it('should not modify other tags when updating', () => {
		const t1 = mockTag({ id: 't1', name: 'One' });
		const t2 = mockTag({ id: 't2', name: 'Two' });
		tags.set([t1, t2]);

		tags.update('t1', { name: 'Changed' });

		expect(tags.items[1].name).toBe('Two');
	});

	it('should clear all tags', () => {
		tags.set([mockTag({ name: 'A' }), mockTag({ name: 'B' })]);
		tags.clear();
		expect(tags.items).toEqual([]);
	});
});
