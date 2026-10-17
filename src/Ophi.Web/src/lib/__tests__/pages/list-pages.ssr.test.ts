// @vitest-environment node
import { describe, it, expect, vi } from 'vitest';
import { render } from 'svelte/server';
import ComparisonsPage from '../../../routes/comparisons/+page.svelte';
import TagsPage from '../../../routes/tags/+page.svelte';

// The server HTML is what a user sees until hydration finishes. A list that only fills from an
// $effect renders its empty state here, then jumps to the real rows.
vi.mock('$app/navigation', () => ({ goto: vi.fn() }));

const base = { user: null, authChecked: true, isPublic: false };

describe('List pages — server render', () => {
	it('should render the comparison groups from the page data, not the empty state', () => {
		const { body } = render(ComparisonsPage, {
			props: { data: { ...base, groups: [{ id: 'g1', name: 'Laptops', productCount: 3 }] } as never }
		});

		expect(body).toContain('Laptops');
		expect(body).not.toContain('No comparison groups yet');
	});

	it('should render the tags from the page data, not the empty state', () => {
		const { body } = render(TagsPage, {
			props: {
				data: { ...base, tags: [{ id: 't1', name: 'Gaming', color: '#3366ff', productCount: 2 }] } as never
			}
		});

		expect(body).toContain('Gaming');
		expect(body).not.toContain('No tags yet');
	});
});
