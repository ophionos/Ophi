import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import TagFilterChips from './TagFilterChips.svelte';
import type { Tag } from '$lib/api/client';

function mockTag(overrides: Partial<Tag> = {}): Tag {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		name: overrides.name ?? 'Test Tag',
		color: overrides.color ?? '#3b82f6',
		weight: overrides.weight ?? 0,
		productCount: overrides.productCount ?? 0,
		...overrides
	} as Tag;
}

describe('TagFilterChips', () => {
	const defaultProps = {
		tags: [] as Tag[],
		selectedTagId: null as string | null,
		onSelectTag: vi.fn()
	};

	describe('block layout (default)', () => {
		it('should render All button', () => {
			render(TagFilterChips, { props: defaultProps });
			expect(screen.getByTestId('tag-filter-all')).toHaveTextContent('All');
		});

		it('should render tag chips', () => {
			const tags = [mockTag({ name: 'Electronics' }), mockTag({ name: 'Sale' })];
			render(TagFilterChips, { props: { ...defaultProps, tags } });

			expect(screen.getByText('Electronics')).toBeInTheDocument();
			expect(screen.getByText('Sale')).toBeInTheDocument();
		});

		it('should call onSelectTag with null when All clicked', async () => {
			const onSelectTag = vi.fn();
			render(TagFilterChips, { props: { ...defaultProps, onSelectTag } });

			await fireEvent.click(screen.getByTestId('tag-filter-all'));

			expect(onSelectTag).toHaveBeenCalledWith(null);
		});

		it('should call onSelectTag with tag id when chip clicked', async () => {
			const onSelectTag = vi.fn();
			const tags = [mockTag({ id: 'tag1', name: 'Electronics' })];
			render(TagFilterChips, { props: { ...defaultProps, tags, onSelectTag } });

			await fireEvent.click(screen.getByText('Electronics'));

			expect(onSelectTag).toHaveBeenCalledWith('tag1');
		});

		it('should sort tags by weight descending then name ascending', () => {
			const tags = [
				mockTag({ name: 'C', weight: 1 }),
				mockTag({ name: 'A', weight: 2 }),
				mockTag({ name: 'B', weight: 2 })
			];
			render(TagFilterChips, { props: { ...defaultProps, tags } });

			const chips = screen.getAllByTestId('tag-filter-chip');
			expect(chips[0]).toHaveTextContent('A');
			expect(chips[1]).toHaveTextContent('B');
			expect(chips[2]).toHaveTextContent('C');
		});
	});

	describe('inline layout', () => {
		it('should show All Tags button only when a tag is selected', () => {
			const tags = [mockTag({ name: 'Test' })];
			render(TagFilterChips, {
				props: { ...defaultProps, tags, selectedTagId: 'some-id', inline: true }
			});

			expect(screen.getByText('All Tags')).toBeInTheDocument();
		});

		it('should hide All Tags button when no tag is selected', () => {
			const tags = [mockTag({ name: 'Test' })];
			render(TagFilterChips, {
				props: { ...defaultProps, tags, selectedTagId: null, inline: true }
			});

			expect(screen.queryByText('All Tags')).not.toBeInTheDocument();
		});
	});
});
