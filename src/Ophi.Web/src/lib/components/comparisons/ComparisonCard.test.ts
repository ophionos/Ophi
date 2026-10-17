import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ComparisonCard from './ComparisonCard.svelte';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));
import type { ComparisonGroup } from '$lib/api/client';

describe('ComparisonCard', () => {
	const mockGroup: ComparisonGroup = {
		id: 'group-123',
		name: 'Headphones Comparison',
		productCount: 5
	};

	it('should render group name', () => {
		render(ComparisonCard, { props: { group: mockGroup } });

		expect(screen.getByText('Headphones Comparison')).toBeInTheDocument();
	});

	it('should render product count with singular form', () => {
		const singleProductGroup = { ...mockGroup, productCount: 1 };
		render(ComparisonCard, { props: { group: singleProductGroup } });

		expect(screen.getByText('1 product')).toBeInTheDocument();
	});

	it('should render product count with plural form', () => {
		render(ComparisonCard, { props: { group: mockGroup } });

		expect(screen.getByText('5 products')).toBeInTheDocument();
	});

	it('should link the group name to the group page', () => {
		render(ComparisonCard, { props: { group: mockGroup } });

		expect(screen.getByRole('link', { name: 'Headphones Comparison' })).toHaveAttribute(
			'href',
			'/comparisons/group-123'
		);
	});

	it('should stretch the link over the whole card', () => {
		render(ComparisonCard, { props: { group: mockGroup } });

		expect(screen.getByRole('link').className).toContain('after:absolute');
	});

	it('should keep the delete button outside the link and above it', () => {
		render(ComparisonCard, { props: { group: mockGroup, onDelete: vi.fn() } });

		const del = screen.getByTitle('Delete');
		expect(del.closest('a')).toBeNull();
		expect(del.className).toContain('relative');
	});

	it('should render delete button when onDelete is provided', () => {
		const onDelete = vi.fn();
		render(ComparisonCard, { props: { group: mockGroup, onDelete } });

		expect(screen.getByTitle('Delete')).toBeInTheDocument();
	});

	it('should not render delete button when onDelete is not provided', () => {
		render(ComparisonCard, { props: { group: mockGroup } });

		expect(screen.queryByTitle('Delete')).not.toBeInTheDocument();
	});

	it('should call onDelete when delete button is clicked', async () => {
		const onDelete = vi.fn();
		render(ComparisonCard, { props: { group: mockGroup, onDelete } });

		await fireEvent.click(screen.getByTitle('Delete'));

		expect(onDelete).toHaveBeenCalledWith(mockGroup);
	});

	it('should render zero products correctly', () => {
		const emptyGroup = { ...mockGroup, productCount: 0 };
		render(ComparisonCard, { props: { group: emptyGroup } });

		expect(screen.getByText('0 products')).toBeInTheDocument();
	});
});
