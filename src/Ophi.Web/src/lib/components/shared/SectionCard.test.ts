import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import SectionCard from './SectionCard.svelte';
import { createRawSnippet } from 'svelte';
import { Bell } from 'lucide-svelte';

function makeBodySnippet(html: string) {
	return createRawSnippet(() => ({
		render: () => html
	}));
}

function makeActionSnippet(html: string) {
	return createRawSnippet(() => ({
		render: () => html
	}));
}

describe('SectionCard', () => {
	const body = makeBodySnippet('<div data-testid="section-body">body content</div>');

	it('should render the title and body', () => {
		render(SectionCard, { props: { title: 'Price History', children: body } });
		expect(screen.getByText('Price History')).toBeInTheDocument();
		expect(screen.getByTestId('section-body')).toBeInTheDocument();
	});

	it('should render a subtitle when provided', () => {
		render(SectionCard, {
			props: { title: 'Per-Domain Health', subtitle: 'Last 7 days of activity', children: body }
		});
		expect(screen.getByText('Per-Domain Health')).toBeInTheDocument();
		expect(screen.getByText('Last 7 days of activity')).toBeInTheDocument();
	});

	it('should render an icon when provided', () => {
		const { container } = render(SectionCard, {
			props: { title: 'Alerts', icon: Bell, children: body }
		});
		expect(container.querySelector('svg')).toBeInTheDocument();
	});

	it('should render the action snippet when provided', () => {
		render(SectionCard, {
			props: {
				title: 'Store URLs',
				action: makeActionSnippet('<button data-testid="action-btn">Add</button>'),
				children: body
			}
		});
		expect(screen.getByTestId('action-btn')).toBeInTheDocument();
	});

	it('should render the body by default for a non-collapsible card', () => {
		render(SectionCard, { props: { title: 'Open Section', children: body } });
		expect(screen.getByTestId('section-body')).toBeInTheDocument();
	});

	it('should hide the body when collapsible and closed, then reveal it on click', async () => {
		render(SectionCard, {
			props: { title: 'Danger Zone', collapsible: true, open: false, children: body }
		});
		expect(screen.queryByTestId('section-body')).not.toBeInTheDocument();

		await fireEvent.click(screen.getByRole('button', { name: /Danger Zone/ }));
		expect(screen.getByTestId('section-body')).toBeInTheDocument();
	});

	it('should apply the testid attribute when provided', () => {
		const { container } = render(SectionCard, {
			props: { title: 'X', testid: 'comparison-section', children: body }
		});
		expect(container.querySelector('[data-testid="comparison-section"]')).toBeInTheDocument();
	});
});
