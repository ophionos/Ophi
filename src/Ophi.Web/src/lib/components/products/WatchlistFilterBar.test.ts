import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import WatchlistFilterBar from './WatchlistFilterBar.svelte';

const baseProps = () => ({
	search: '',
	status: '' as const,
	selectedTagId: null,
	storeId: null,
	sortBy: '',
	sortDirection: 'asc',
	favouriteOnly: false,
	tags: [{ id: 't1', name: 'Desk', color: '#000', weight: 0, productCount: 1 }],
	stores: [
		{ storeId: 'amazon', name: 'Amazon' },
		{ storeId: 'shop', name: 'The Shop' }
	],
	activeFilters: [],
	onSearchChange: vi.fn(),
	onStatusChange: vi.fn(),
	onTagChange: vi.fn(),
	onStoreChange: vi.fn(),
	onSortChange: vi.fn(),
	onFavouriteChange: vi.fn(),
	onClearAll: vi.fn()
});

describe('WatchlistFilterBar', () => {
	it('should report the text filter as it is typed', async () => {
		const props = baseProps();
		render(WatchlistFilterBar, { props });

		await fireEvent.input(screen.getByTestId('search-input'), { target: { value: 'lamp' } });

		expect(props.onSearchChange).toHaveBeenCalledWith('lamp');
	});

	it('should filter by status, tag and store from their selects', async () => {
		const props = baseProps();
		render(WatchlistFilterBar, { props });

		await fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'paused' } });
		await fireEvent.change(screen.getByLabelText('Tag'), { target: { value: 't1' } });
		await fireEvent.change(screen.getByLabelText('Store'), { target: { value: 'shop' } });

		expect(props.onStatusChange).toHaveBeenCalledWith('paused');
		expect(props.onTagChange).toHaveBeenCalledWith('t1');
		expect(props.onStoreChange).toHaveBeenCalledWith('shop');
	});

	it('should hide the store select when the account uses one store', () => {
		render(WatchlistFilterBar, {
			props: { ...baseProps(), stores: [{ storeId: 'shop', name: 'The Shop' }] }
		});
		expect(screen.queryByLabelText('Store')).not.toBeInTheDocument();
	});

	it('should offer favourites first as an explicit sort', async () => {
		const props = baseProps();
		render(WatchlistFilterBar, { props });

		await fireEvent.change(screen.getByLabelText('Sort by'), {
			target: { value: 'favourites:asc' }
		});

		expect(props.onSortChange).toHaveBeenCalledWith('favourites', 'asc');
	});

	it('should show each active filter as a removable chip with clear all', async () => {
		const onRemove = vi.fn();
		const props = {
			...baseProps(),
			activeFilters: [
				{ key: 'status', label: 'Status: Paused', onRemove },
				{ key: 'store', label: 'Store: The Shop', onRemove: vi.fn() }
			]
		};
		render(WatchlistFilterBar, { props });

		await fireEvent.click(screen.getByRole('button', { name: 'Remove filter: Status: Paused' }));
		await fireEvent.click(screen.getByTestId('clear-all-filters'));

		expect(onRemove).toHaveBeenCalledOnce();
		expect(props.onClearAll).toHaveBeenCalledOnce();
	});

	it('should not show the chip row without active filters', () => {
		render(WatchlistFilterBar, { props: baseProps() });
		expect(screen.queryByTestId('active-filters')).not.toBeInTheDocument();
	});
});
