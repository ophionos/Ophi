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
	describe('preview', () => {
		const priced: ComparisonGroup = {
			...mockGroup,
			bestPriceProductId: 'p1',
			bestPriceProductName: 'Logitech MX Master 3S',
			bestPrice: 45,
			currency: 'USD',
			sparkline: [
				{ date: '2026-10-01', price: 50 },
				{ date: '2026-10-02', price: 48 },
				{ date: '2026-10-03', price: 45 }
			]
		};

		it('should show the cheapest price and product when the group has a best price', () => {
			render(ComparisonCard, { props: { group: priced } });

			const best = screen.getByTestId('comparison-card-best');
			expect(best).toHaveTextContent('Cheapest $45.00');
			expect(best).toHaveTextContent('Logitech MX Master 3S');
		});

		it('should not use winner wording when the group has a best price', () => {
			const { container } = render(ComparisonCard, { props: { group: priced } });

			expect(container.textContent).not.toMatch(/best|winner/i);
		});

		it('should draw a hidden sparkline when the series has enough points', () => {
			const { container } = render(ComparisonCard, { props: { group: priced } });

			expect(container.querySelector('svg polyline')).not.toBeNull();
			expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
		});

		it('should not draw a sparkline when the series is too short', () => {
			const { container } = render(ComparisonCard, {
				props: { group: { ...priced, sparkline: priced.sparkline!.slice(0, 1) } }
			});

			expect(container.querySelector('polyline')).toBeNull();
		});

		it('should draw a flat sparkline without NaN when every price is equal', () => {
			const flat = priced.sparkline!.map((p) => ({ ...p, price: 45 }));
			const { container } = render(ComparisonCard, { props: { group: { ...priced, sparkline: flat } } });

			expect(container.querySelector('polyline')?.getAttribute('points')).not.toContain('NaN');
		});

		it('should let the card shrink below its text width in a grid column', () => {
			// A grid item's minimum is its min-content width, and the nowrap preview line makes
			// that the full text: the card then pushed a phone-width page sideways.
			render(ComparisonCard, { props: { group: priced } });

			expect(screen.getByTestId('comparison-card').className).toContain('min-w-0');
		});

		it('should show no preview when the group has no best price', () => {
			render(ComparisonCard, { props: { group: mockGroup } });

			expect(screen.queryByTestId('comparison-card-best')).toBeNull();
		});
	});
});

