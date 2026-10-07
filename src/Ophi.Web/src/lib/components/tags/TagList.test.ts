import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import TagList from './TagList.svelte';
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

describe('TagList', () => {
	const defaultProps = {
		tags: [] as Tag[],
		onEdit: vi.fn(),
		onDelete: vi.fn()
	};

	it('should render empty state when no tags', () => {
		render(TagList, { props: defaultProps });
		expect(screen.getByText(/No tags created yet/)).toBeInTheDocument();
	});

	it('should render tags', () => {
		const tags = [mockTag({ name: 'Electronics' }), mockTag({ name: 'Sale' })];
		render(TagList, { props: { ...defaultProps, tags } });

		expect(screen.getByText('Electronics')).toBeInTheDocument();
		expect(screen.getByText('Sale')).toBeInTheDocument();
	});

	it('should sort tags by weight descending then name ascending', () => {
		const tags = [
			mockTag({ name: 'C', weight: 1 }),
			mockTag({ name: 'A', weight: 2 }),
			mockTag({ name: 'B', weight: 2 })
		];
		render(TagList, { props: { ...defaultProps, tags } });

		const items = screen.getAllByTestId('tag-list-item');
		expect(items[0]).toHaveTextContent('A');
		expect(items[1]).toHaveTextContent('B');
		expect(items[2]).toHaveTextContent('C');
	});

	it('should display product count', () => {
		const tags = [mockTag({ name: 'Test', productCount: 5 })];
		render(TagList, { props: { ...defaultProps, tags } });

		expect(screen.getByText('5 products')).toBeInTheDocument();
	});

	it('should display singular product count', () => {
		const tags = [mockTag({ name: 'Test', productCount: 1 })];
		render(TagList, { props: { ...defaultProps, tags } });

		expect(screen.getByText('1 product')).toBeInTheDocument();
	});

	it('should call onEdit when edit button clicked', async () => {
		const onEdit = vi.fn();
		const tag = mockTag({ name: 'Editable' });
		render(TagList, { props: { ...defaultProps, tags: [tag], onEdit } });

		await fireEvent.click(screen.getByTestId('tag-edit-button'));

		expect(onEdit).toHaveBeenCalledWith(tag);
	});

	it('should call onDelete when delete button clicked', async () => {
		const onDelete = vi.fn();
		const tag = mockTag({ name: 'Deletable' });
		render(TagList, { props: { ...defaultProps, tags: [tag], onDelete } });

		await fireEvent.click(screen.getByTestId('tag-delete-button'));

		expect(onDelete).toHaveBeenCalledWith(tag);
	});
});
