import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import TagPicker from './TagPicker.svelte';
import type { Tag, ProductTag } from '$lib/api/client';

function mockTag(overrides: Partial<Tag> = {}): Tag {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		name: overrides.name ?? 'Test Tag',
		color: overrides.color ?? '#3b82f6',
		weight: 0,
		productCount: 0,
		...overrides
	} as Tag;
}

function mockProductTag(overrides: Partial<ProductTag> = {}): ProductTag {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		name: overrides.name ?? 'Selected Tag',
		color: overrides.color ?? '#ef4444',
		...overrides
	} as ProductTag;
}

describe('TagPicker', () => {
	const defaultProps = {
		availableTags: [] as Tag[],
		selectedTags: [] as ProductTag[],
		onAdd: vi.fn().mockResolvedValue(undefined),
		onRemove: vi.fn().mockResolvedValue(undefined)
	};

	it('should render add tag button', () => {
		render(TagPicker, { props: defaultProps });
		expect(screen.getByTestId('add-tag-button')).toHaveTextContent('Add tag');
	});

	it('should render selected tags as badges', () => {
		const selectedTags = [mockProductTag({ name: 'Electronics' }), mockProductTag({ name: 'Sale' })];
		render(TagPicker, { props: { ...defaultProps, selectedTags } });

		expect(screen.getByText('Electronics')).toBeInTheDocument();
		expect(screen.getByText('Sale')).toBeInTheDocument();
	});

	it('should not show dropdown initially', () => {
		render(TagPicker, { props: defaultProps });
		expect(screen.queryByTestId('tag-dropdown')).not.toBeInTheDocument();
	});

	it('should open dropdown when add tag button clicked', async () => {
		const availableTags = [mockTag({ name: 'Available' })];
		render(TagPicker, { props: { ...defaultProps, availableTags } });

		await fireEvent.click(screen.getByTestId('add-tag-button'));

		expect(screen.getByTestId('tag-dropdown')).toBeInTheDocument();
	});

	it('should close dropdown when add tag button clicked again', async () => {
		const availableTags = [mockTag({ name: 'Available' })];
		render(TagPicker, { props: { ...defaultProps, availableTags } });

		await fireEvent.click(screen.getByTestId('add-tag-button'));
		await fireEvent.click(screen.getByTestId('add-tag-button'));

		expect(screen.queryByTestId('tag-dropdown')).not.toBeInTheDocument();
	});

	it('should show only unselected tags in dropdown', async () => {
		const availableTags = [mockTag({ id: 't1', name: 'Available' }), mockTag({ id: 't2', name: 'Selected' })];
		const selectedTags = [mockProductTag({ id: 't2', name: 'Selected' })];
		render(TagPicker, { props: { ...defaultProps, availableTags, selectedTags } });

		await fireEvent.click(screen.getByTestId('add-tag-button'));

		const options = screen.getAllByRole('option');
		expect(options).toHaveLength(1);
		expect(options[0]).toHaveTextContent('Available');
	});

	it('should show empty message when all tags are selected', async () => {
		const availableTags = [mockTag({ id: 't1', name: 'Test' })];
		const selectedTags = [mockProductTag({ id: 't1', name: 'Test' })];
		render(TagPicker, { props: { ...defaultProps, availableTags, selectedTags } });

		await fireEvent.click(screen.getByTestId('add-tag-button'));

		expect(screen.getByText('No more tags available')).toBeInTheDocument();
	});

	it('should call onAdd when a tag option is clicked', async () => {
		const onAdd = vi.fn().mockResolvedValue(undefined);
		const availableTags = [mockTag({ id: 'tag-to-add', name: 'New Tag' })];
		render(TagPicker, { props: { ...defaultProps, availableTags, onAdd } });

		await fireEvent.click(screen.getByTestId('add-tag-button'));
		await fireEvent.click(screen.getByText('New Tag'));

		expect(onAdd).toHaveBeenCalledWith('tag-to-add');
	});

	it('should call onRemove when remove button on badge is clicked', async () => {
		const onRemove = vi.fn().mockResolvedValue(undefined);
		const selectedTags = [mockProductTag({ id: 'tag-to-remove', name: 'Remove Me' })];
		render(TagPicker, { props: { ...defaultProps, selectedTags, onRemove } });

		await fireEvent.click(screen.getByRole('button', { name: /Remove tag Remove Me/ }));

		expect(onRemove).toHaveBeenCalledWith('tag-to-remove');
	});

	it('should close dropdown on Escape key', async () => {
		const availableTags = [mockTag({ name: 'Test' })];
		render(TagPicker, { props: { ...defaultProps, availableTags } });

		await fireEvent.click(screen.getByTestId('add-tag-button'));
		expect(screen.getByTestId('tag-dropdown')).toBeInTheDocument();

		await fireEvent.keyDown(screen.getByTestId('add-tag-button'), { key: 'Escape' });

		expect(screen.queryByTestId('tag-dropdown')).not.toBeInTheDocument();
	});
});
