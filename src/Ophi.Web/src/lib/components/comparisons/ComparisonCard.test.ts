import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ComparisonCard from './ComparisonCard.svelte';
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

	it('should render view button when onView is provided', () => {
		const onView = vi.fn();
		render(ComparisonCard, { props: { group: mockGroup, onView } });

		expect(screen.getByTitle('View')).toBeInTheDocument();
	});

	it('should not render view button when onView is not provided', () => {
		render(ComparisonCard, { props: { group: mockGroup } });

		expect(screen.queryByTitle('View')).not.toBeInTheDocument();
	});

	it('should call onView when view button is clicked', async () => {
		const onView = vi.fn();
		render(ComparisonCard, { props: { group: mockGroup, onView } });

		await fireEvent.click(screen.getByTitle('View'));

		expect(onView).toHaveBeenCalledWith(mockGroup);
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
