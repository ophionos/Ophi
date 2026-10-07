import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { Tag, TrendingDown } from 'lucide-svelte';
import EmptyState from './EmptyState.svelte';

describe('EmptyState', () => {
	it('should render the title as a heading and the description', () => {
		render(EmptyState, {
			props: { icon: Tag, title: 'No tags yet', description: 'Tags group your products.' }
		});
		expect(screen.getByRole('heading', { name: 'No tags yet' })).toBeInTheDocument();
		expect(screen.getByText('Tags group your products.')).toBeInTheDocument();
	});

	it('should render the action snippet when given', () => {
		const children = createRawSnippet(() => ({
			render: () => '<button data-testid="cta">Create tag</button>'
		}));
		render(EmptyState, { props: { icon: Tag, title: 'No tags yet', children } });
		expect(screen.getByTestId('cta')).toBeInTheDocument();
	});

	it('should render the badge icon only when given', () => {
		const { container, unmount } = render(EmptyState, { props: { icon: Tag, title: 'x' } });
		expect(container.querySelectorAll('svg')).toHaveLength(1);
		unmount();

		const withBadge = render(EmptyState, {
			props: { icon: Tag, badgeIcon: TrendingDown, title: 'x' }
		});
		expect(withBadge.container.querySelectorAll('svg')).toHaveLength(2);
	});

	it('should use compact padding by default and generous padding for size lg', () => {
		const { unmount } = render(EmptyState, { props: { icon: Tag, title: 'x', testid: 'es' } });
		expect(screen.getByTestId('es')).toHaveClass('py-10');
		unmount();

		render(EmptyState, { props: { icon: Tag, title: 'x', size: 'lg', testid: 'es' } });
		expect(screen.getByTestId('es')).toHaveClass('py-20');
	});

	it('should drop its own border and background when framed is false', () => {
		render(EmptyState, { props: { icon: Tag, title: 'x', framed: false, testid: 'es' } });
		expect(screen.getByTestId('es')).not.toHaveClass('border');
		expect(screen.getByTestId('es')).not.toHaveClass('bg-surface-1');
	});
});
