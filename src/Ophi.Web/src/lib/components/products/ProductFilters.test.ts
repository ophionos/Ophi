import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ProductFilters from './ProductFilters.svelte';

describe('ProductFilters', () => {
	const defaultProps = {
		search: '',
		status: '',
		sortBy: '',
		sortDirection: 'asc',
		onSearchChange: vi.fn(),
		onStatusChange: vi.fn(),
		onSortChange: vi.fn()
	};

	beforeEach(() => {
		vi.resetAllMocks();
	});

	describe('search input', () => {
		it('should render search input', () => {
			render(ProductFilters, { props: defaultProps });

			const input = screen.getByTestId('search-input');
			expect(input).toBeInTheDocument();
			expect(input).toHaveAttribute('placeholder', 'Search products...');
		});

		it('should display current search value', () => {
			render(ProductFilters, { props: { ...defaultProps, search: 'headphones' } });

			const input = screen.getByTestId('search-input');
			expect(input).toHaveValue('headphones');
		});

		it('should call onSearchChange when typing', async () => {
			render(ProductFilters, { props: defaultProps });

			const input = screen.getByTestId('search-input');
			await fireEvent.input(input, { target: { value: 'sony' } });

			expect(defaultProps.onSearchChange).toHaveBeenCalledWith('sony');
		});

		it('should show clear button when search has value', () => {
			render(ProductFilters, { props: { ...defaultProps, search: 'test' } });

			const clearButton = screen.getByRole('button', { name: /clear search/i });
			expect(clearButton).toBeInTheDocument();
		});

		it('should not show clear button when search is empty', () => {
			render(ProductFilters, { props: defaultProps });

			const clearButton = screen.queryByRole('button', { name: /clear search/i });
			expect(clearButton).not.toBeInTheDocument();
		});

		it('should call onSearchChange with empty string when clear is clicked', async () => {
			render(ProductFilters, { props: { ...defaultProps, search: 'test' } });

			const clearButton = screen.getByRole('button', { name: /clear search/i });
			await fireEvent.click(clearButton);

			expect(defaultProps.onSearchChange).toHaveBeenCalledWith('');
		});
	});

	describe('status filter chips', () => {
		it('should render all status filter chips', () => {
			render(ProductFilters, { props: defaultProps });

			expect(screen.getByTestId('status-chip-all')).toBeInTheDocument();
			expect(screen.getByTestId('status-chip-active')).toBeInTheDocument();
			expect(screen.getByTestId('status-chip-paused')).toBeInTheDocument();
			expect(screen.getByTestId('status-chip-error')).toBeInTheDocument();
		});

		it('should highlight selected status chip', () => {
			render(ProductFilters, { props: { ...defaultProps, status: 'active' } });

			const activeChip = screen.getByTestId('status-chip-active');
			expect(activeChip).toHaveClass('bg-brand');
		});

		it('should highlight All chip when no status is selected', () => {
			render(ProductFilters, { props: defaultProps });

			const allChip = screen.getByTestId('status-chip-all');
			expect(allChip).toHaveClass('bg-brand');
		});

		it('should call onStatusChange when chip is clicked', async () => {
			render(ProductFilters, { props: defaultProps });

			const pausedChip = screen.getByTestId('status-chip-paused');
			await fireEvent.click(pausedChip);

			expect(defaultProps.onStatusChange).toHaveBeenCalledWith('paused');
		});

		it('should call onStatusChange with empty string when All is clicked', async () => {
			render(ProductFilters, { props: { ...defaultProps, status: 'active' } });

			const allChip = screen.getByTestId('status-chip-all');
			await fireEvent.click(allChip);

			expect(defaultProps.onStatusChange).toHaveBeenCalledWith('');
		});
	});

	describe('sort dropdown', () => {
		it('should render sort dropdown', () => {
			render(ProductFilters, { props: defaultProps });

			const select = screen.getByTestId('sort-select');
			expect(select).toBeInTheDocument();
		});

		it('should have Recently Updated as default option', () => {
			render(ProductFilters, { props: defaultProps });

			const select = screen.getByTestId('sort-select') as HTMLSelectElement;
			expect(select.value).toBe('');
		});

		it('should show current sort value', () => {
			render(ProductFilters, {
				props: { ...defaultProps, sortBy: 'price', sortDirection: 'desc' }
			});

			const select = screen.getByTestId('sort-select') as HTMLSelectElement;
			expect(select.value).toBe('price:desc');
		});

		it('should call onSortChange when sort is changed to asc', async () => {
			render(ProductFilters, { props: defaultProps });

			const select = screen.getByTestId('sort-select');
			await fireEvent.change(select, { target: { value: 'name:asc' } });

			expect(defaultProps.onSortChange).toHaveBeenCalledWith('name', 'asc');
		});

		it('should call onSortChange when sort is changed to desc', async () => {
			render(ProductFilters, { props: defaultProps });

			const select = screen.getByTestId('sort-select');
			await fireEvent.change(select, { target: { value: 'price:desc' } });

			expect(defaultProps.onSortChange).toHaveBeenCalledWith('price', 'desc');
		});

		it('should call onSortChange with empty sortBy when Recently Updated is selected', async () => {
			render(ProductFilters, {
				props: { ...defaultProps, sortBy: 'name', sortDirection: 'asc' }
			});

			const select = screen.getByTestId('sort-select');
			await fireEvent.change(select, { target: { value: '' } });

			expect(defaultProps.onSortChange).toHaveBeenCalledWith('', 'asc');
		});

		it('should include all sort options', () => {
			render(ProductFilters, { props: defaultProps });

			const select = screen.getByTestId('sort-select');
			const options = select.querySelectorAll('option');

			// Recently Updated + 4 sort options (each with asc/desc) = 1 + 8 = 9
			// Sort options: Name, Price, Date Added, Price Change
			expect(options.length).toBe(9);
		});
	});

	describe('accessibility', () => {
		it('should have sr-only label for search input', () => {
			render(ProductFilters, { props: defaultProps });

			expect(screen.getByLabelText('Search products')).toBeInTheDocument();
		});

		it('should have sr-only label for sort select', () => {
			render(ProductFilters, { props: defaultProps });

			expect(screen.getByLabelText('Sort by')).toBeInTheDocument();
		});
	});
});
