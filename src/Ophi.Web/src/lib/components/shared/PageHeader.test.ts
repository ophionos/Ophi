import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import PageHeader from './PageHeader.svelte';

const actions = createRawSnippet(() => ({
	render: () => '<button data-testid="header-action">Add</button>'
}));

describe('PageHeader', () => {
	it('should render the title as the level-one heading', () => {
		render(PageHeader, { props: { title: 'Tags' } });
		expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Tags');
	});

	it('should render the description when provided', () => {
		render(PageHeader, { props: { title: 'Tags', description: 'Organise your products' } });
		expect(screen.getByText('Organise your products')).toBeInTheDocument();
	});

	it('should not render a description element when none is given', () => {
		const { container } = render(PageHeader, { props: { title: 'Tags' } });
		expect(container.querySelector('p')).toBeNull();
	});

	it('should render the actions when provided', () => {
		render(PageHeader, { props: { title: 'Tags', actions } });
		expect(screen.getByTestId('header-action')).toBeInTheDocument();
	});

	it('should let the actions wrap under the title on a narrow screen', () => {
		render(PageHeader, { props: { title: 'Tags', actions } });
		expect(screen.getByTestId('page-header').className).toContain('flex-wrap');
	});
});
