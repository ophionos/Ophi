import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import DashboardStatsBar from './DashboardStatsBar.svelte';

describe('DashboardStatsBar', () => {
	const baseProps = {
		totalProducts: 42,
		priceDrops: 5,
		alertCount: 3,
		atLowestCount: 2,
		statsFilter: null as null | 'drops' | 'alerts' | 'lowest',
		onFilterChange: vi.fn()
	};

	it('renders all counts', () => {
		render(DashboardStatsBar, { props: baseProps });
		expect(screen.getByText('42')).toBeInTheDocument();
		expect(screen.getByText('5')).toBeInTheDocument();
		expect(screen.getByText('3')).toBeInTheDocument();
		expect(screen.getByText('2')).toBeInTheDocument();
	});

	it('invokes onFilterChange with "drops" when Drops button clicked', async () => {
		const onFilterChange = vi.fn();
		render(DashboardStatsBar, { props: { ...baseProps, onFilterChange } });
		await fireEvent.click(screen.getByTitle(/products with price drops/));
		expect(onFilterChange).toHaveBeenCalledWith('drops');
	});

	it('toggles off when clicking the active filter again', async () => {
		const onFilterChange = vi.fn();
		render(DashboardStatsBar, {
			props: { ...baseProps, statsFilter: 'drops', onFilterChange }
		});
		await fireEvent.click(screen.getByTitle(/Clear filter/));
		expect(onFilterChange).toHaveBeenCalledWith(null);
	});

	it('invokes onFilterChange with "lowest" via data-testid', async () => {
		const onFilterChange = vi.fn();
		render(DashboardStatsBar, { props: { ...baseProps, onFilterChange } });
		await fireEvent.click(screen.getByTestId('stats-filter-lowest'));
		expect(onFilterChange).toHaveBeenCalledWith('lowest');
	});

	it('should link to the alerts page beside the alerts filter', () => {
		render(DashboardStatsBar, { props: baseProps });
		expect(screen.getByTestId('stats-view-all-alerts')).toHaveAttribute('href', '/alerts');
	});
});
