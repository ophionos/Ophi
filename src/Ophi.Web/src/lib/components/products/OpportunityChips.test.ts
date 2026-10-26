import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import OpportunityChips from './OpportunityChips.svelte';

function baseProps(overrides: Record<string, unknown> = {}) {
	return {
		priceDropCount: 1,
		nearTargetCount: 1,
		needsActionCount: 1,
		atLowestCount: 1,
		active: null,
		onSelect: vi.fn(),
		...overrides
	};
}

describe('OpportunityChips', () => {
	it('should show the four chips in order with their labels and counts', () => {
		render(OpportunityChips, {
			props: baseProps({ priceDropCount: 5, nearTargetCount: 1, needsActionCount: 2, atLowestCount: 3 })
		});

		const chips = screen.getByTestId('opportunity-chips').querySelectorAll('button');
		expect([...chips].map((c) => c.dataset.testid)).toEqual([
			'opportunity-drop',
			'opportunity-near',
			'opportunity-action',
			'opportunity-lowest'
		]);
		expect(screen.getByTestId('opportunity-drop')).toHaveTextContent('Price dropped (7 days)');
		expect(screen.getByTestId('opportunity-drop')).toHaveTextContent('5');
		expect(screen.getByTestId('opportunity-near')).toHaveTextContent('Near target');
		expect(screen.getByTestId('opportunity-action')).toHaveTextContent('Needs action');
		expect(screen.getByTestId('opportunity-action')).toHaveTextContent('2');
		expect(screen.getByTestId('opportunity-lowest')).toHaveTextContent('At a real low');
		expect(screen.getByTestId('opportunity-lowest')).toHaveTextContent('3');
	});

	it('should hide a chip whose count is zero', () => {
		render(OpportunityChips, { props: baseProps({ atLowestCount: 0, needsActionCount: 0 }) });

		expect(screen.queryByTestId('opportunity-lowest')).not.toBeInTheDocument();
		expect(screen.queryByTestId('opportunity-action')).not.toBeInTheDocument();
		expect(screen.getByTestId('opportunity-near')).toBeInTheDocument();
	});

	it('should render nothing when every count is zero', () => {
		render(OpportunityChips, {
			props: baseProps({ priceDropCount: 0, nearTargetCount: 0, needsActionCount: 0, atLowestCount: 0 })
		});

		expect(screen.queryByTestId('opportunity-chips')).not.toBeInTheDocument();
	});

	it('should keep the active chip visible at zero so it can be turned off', async () => {
		const onSelect = vi.fn();
		render(OpportunityChips, {
			props: baseProps({ atLowestCount: 0, active: 'lowest', onSelect })
		});

		const chip = screen.getByTestId('opportunity-lowest');
		expect(chip).toHaveAttribute('aria-pressed', 'true');
		await fireEvent.click(chip);
		expect(onSelect).toHaveBeenCalledWith(null);
	});

	it.each(['drop', 'near', 'action', 'lowest'])(
		'should select the %s chip when it is clicked',
		async (key) => {
			const onSelect = vi.fn();
			render(OpportunityChips, { props: baseProps({ onSelect }) });

			await fireEvent.click(screen.getByTestId(`opportunity-${key}`));

			expect(onSelect).toHaveBeenCalledWith(key);
		}
	);
});
