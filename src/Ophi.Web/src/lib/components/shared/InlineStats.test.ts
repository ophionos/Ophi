import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/svelte';
import { Tag } from 'lucide-svelte';
import InlineStats from './InlineStats.svelte';

const stats = [
	{ label: 'Total Tags', value: 4, icon: Tag, testid: 'stat-total-tags' },
	{ label: 'Tagged Products', value: 9, testid: 'stat-tagged-products' }
];

describe('InlineStats', () => {
	it('should render each stat label with its value', () => {
		render(InlineStats, { props: { stats } });
		expect(within(screen.getByTestId('stat-total-tags')).getByText('4')).toBeInTheDocument();
		expect(within(screen.getByTestId('stat-total-tags')).getByText('Total Tags')).toBeInTheDocument();
		expect(within(screen.getByTestId('stat-tagged-products')).getByText('9')).toBeInTheDocument();
	});

	it('should render the icon only for stats that have one', () => {
		render(InlineStats, { props: { stats } });
		expect(screen.getByTestId('stat-total-tags').querySelector('svg')).not.toBeNull();
		expect(screen.getByTestId('stat-tagged-products').querySelector('svg')).toBeNull();
	});

	it('should put a divider between stats but not before the first', () => {
		const { container } = render(InlineStats, { props: { stats } });
		expect(container.querySelectorAll('[data-divider]')).toHaveLength(1);
	});
});
