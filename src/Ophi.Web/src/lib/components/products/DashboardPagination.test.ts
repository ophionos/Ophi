import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import DashboardPagination from './DashboardPagination.svelte';

describe('DashboardPagination', () => {
	it('shows current and total pages', () => {
		render(DashboardPagination, {
			props: { currentPage: 2, totalPages: 5, onPageChange: vi.fn() }
		});
		expect(screen.getByText('Page 2 of 5')).toBeInTheDocument();
	});

	it('disables prev on first page', () => {
		render(DashboardPagination, {
			props: { currentPage: 1, totalPages: 5, onPageChange: vi.fn() }
		});
		expect(screen.getByTestId('pagination-prev')).toBeDisabled();
		expect(screen.getByTestId('pagination-next')).not.toBeDisabled();
	});

	it('disables next on last page', () => {
		render(DashboardPagination, {
			props: { currentPage: 5, totalPages: 5, onPageChange: vi.fn() }
		});
		expect(screen.getByTestId('pagination-prev')).not.toBeDisabled();
		expect(screen.getByTestId('pagination-next')).toBeDisabled();
	});

	it('calls onPageChange with next page', async () => {
		const onPageChange = vi.fn();
		render(DashboardPagination, {
			props: { currentPage: 2, totalPages: 5, onPageChange }
		});
		await fireEvent.click(screen.getByTestId('pagination-next'));
		expect(onPageChange).toHaveBeenCalledWith(3);
	});

	it('calls onPageChange with previous page', async () => {
		const onPageChange = vi.fn();
		render(DashboardPagination, {
			props: { currentPage: 3, totalPages: 5, onPageChange }
		});
		await fireEvent.click(screen.getByTestId('pagination-prev'));
		expect(onPageChange).toHaveBeenCalledWith(2);
	});
});
