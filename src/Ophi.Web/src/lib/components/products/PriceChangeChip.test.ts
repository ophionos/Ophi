import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import PriceChangeChip from './PriceChangeChip.svelte';

describe('PriceChangeChip', () => {
	it('should show a filled chip with a down arrow when the price dropped', () => {
		render(PriceChangeChip, { props: { change: -9.12 } });
		const chip = screen.getByTestId('price-change-chip');
		expect(chip).toHaveTextContent('9.1%');
		expect(chip).toHaveAccessibleName('Down 9.1%');
		expect(chip.className).toContain('bg-price-drop ');
	});

	it('should show a light chip when the price rose', () => {
		render(PriceChangeChip, { props: { change: 3 } });
		const chip = screen.getByTestId('price-change-chip');
		expect(chip).toHaveAccessibleName('Up 3.0%');
		expect(chip.className).toContain('bg-price-increase-subtle');
	});

	it('should show a dash when there is no change', () => {
		render(PriceChangeChip, { props: { change: null } });
		expect(screen.queryByTestId('price-change-chip')).not.toBeInTheDocument();
		expect(screen.getByTestId('price-change-none')).toHaveAccessibleName('No change');
	});
});
