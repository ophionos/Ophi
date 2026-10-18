import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import OpportunityChips from './OpportunityChips.svelte';

describe('OpportunityChips', () => {
	it('should show a chip per opportunity with its count', () => {
		render(OpportunityChips, {
			props: { atLowestCount: 3, nearTargetCount: 1, active: null, onSelect: vi.fn() }
		});

		expect(screen.getByTestId('opportunity-lowest')).toHaveTextContent('3 at their 90-day low');
		expect(screen.getByTestId('opportunity-near')).toHaveTextContent('1 alert near target');
	});

	it('should hide a chip whose count is zero', () => {
		render(OpportunityChips, {
			props: { atLowestCount: 0, nearTargetCount: 2, active: null, onSelect: vi.fn() }
		});

		expect(screen.queryByTestId('opportunity-lowest')).not.toBeInTheDocument();
		expect(screen.getByTestId('opportunity-near')).toHaveTextContent('2 alerts near target');
	});

	it('should render nothing when every count is zero', () => {
		render(OpportunityChips, {
			props: { atLowestCount: 0, nearTargetCount: 0, active: null, onSelect: vi.fn() }
		});

		expect(screen.queryByTestId('opportunity-chips')).not.toBeInTheDocument();
	});

	it('should keep the active chip visible at zero so it can be turned off', async () => {
		const onSelect = vi.fn();
		render(OpportunityChips, {
			props: { atLowestCount: 0, nearTargetCount: 0, active: 'lowest', onSelect }
		});

		const chip = screen.getByTestId('opportunity-lowest');
		expect(chip).toHaveAttribute('aria-pressed', 'true');
		await fireEvent.click(chip);
		expect(onSelect).toHaveBeenCalledWith(null);
	});

	it('should select a chip when it is clicked', async () => {
		const onSelect = vi.fn();
		render(OpportunityChips, {
			props: { atLowestCount: 2, nearTargetCount: 0, active: null, onSelect }
		});

		await fireEvent.click(screen.getByTestId('opportunity-lowest'));
		expect(onSelect).toHaveBeenCalledWith('lowest');
	});
});
