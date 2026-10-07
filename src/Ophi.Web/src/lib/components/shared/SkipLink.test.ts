import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import SkipLink from './SkipLink.svelte';

describe('SkipLink', () => {
	it('should render a link targeting #main-content', () => {
		render(SkipLink);
		const link = screen.getByText('Skip to main content');
		expect(link).toBeInTheDocument();
		expect(link).toHaveAttribute('href', '#main-content');
	});

	it('should be a link element', () => {
		render(SkipLink);
		const link = screen.getByRole('link', { name: 'Skip to main content' });
		expect(link).toBeInTheDocument();
	});

	it('should have sr-only class for visual hiding', () => {
		render(SkipLink);
		const link = screen.getByText('Skip to main content');
		expect(link.className).toContain('sr-only');
	});
});
