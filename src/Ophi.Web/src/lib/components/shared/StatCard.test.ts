import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import StatCard from './StatCard.svelte';

describe('StatCard', () => {
	it('should render the label', () => {
		render(StatCard, { props: { label: 'Lowest', value: 'USD 189.00' } });

		expect(screen.getByText('Lowest')).toBeInTheDocument();
	});

	it('should render the value', () => {
		render(StatCard, { props: { label: 'Lowest', value: 'USD 189.00' } });

		expect(screen.getByText('USD 189.00')).toBeInTheDocument();
	});

	it('should render a numeric value', () => {
		render(StatCard, { props: { label: 'Total Stores', value: 5 } });

		expect(screen.getByText('5')).toBeInTheDocument();
	});

	it('should render the value with tabular figures so stat columns align', () => {
		render(StatCard, { props: { label: 'Lowest', value: 'USD 189.00' } });

		expect(screen.getByText('USD 189.00').className).toContain('tabular-nums');
	});

	it('should apply the valueClass to the value element', () => {
		render(StatCard, {
			props: { label: 'Lowest', value: 'USD 189.00', valueClass: 'text-green-600' }
		});

		expect(screen.getByText('USD 189.00').className).toContain('text-green-600');
	});

	it('should render custom children instead of the value when provided', () => {
		const children = createRawSnippet(() => ({
			render: () => `<span>custom body</span>`
		}));

		render(StatCard, { props: { label: 'Status', value: 'ignored', children } });

		expect(screen.getByText('custom body')).toBeInTheDocument();
		expect(screen.queryByText('ignored')).not.toBeInTheDocument();
	});
});
