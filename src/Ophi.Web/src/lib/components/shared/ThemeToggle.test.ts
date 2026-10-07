import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ThemeToggle from './ThemeToggle.svelte';
import { theme } from '$lib/stores/theme.svelte';

describe('ThemeToggle', () => {
	beforeEach(() => {
		// Reset to light mode before each test
		theme.set('light');
	});

	describe('rendering', () => {
		it('should render a button', () => {
			render(ThemeToggle);
			expect(screen.getByRole('button')).toBeInTheDocument();
		});

		it('should have accessible aria-label in light mode', () => {
			theme.set('light');
			render(ThemeToggle);
			expect(screen.getByLabelText('Switch to dark mode')).toBeInTheDocument();
		});

		it('should have accessible aria-label in dark mode', () => {
			theme.set('dark');
			render(ThemeToggle);
			expect(screen.getByLabelText('Switch to light mode')).toBeInTheDocument();
		});

		it('should have title attribute in light mode', () => {
			theme.set('light');
			render(ThemeToggle);
			expect(screen.getByTitle('Switch to dark mode')).toBeInTheDocument();
		});

		it('should have title attribute in dark mode', () => {
			theme.set('dark');
			render(ThemeToggle);
			expect(screen.getByTitle('Switch to light mode')).toBeInTheDocument();
		});
	});

	describe('icon display', () => {
		it('should render an SVG icon', () => {
			const { container } = render(ThemeToggle);
			const svg = container.querySelector('svg');
			expect(svg).toBeInTheDocument();
		});
	});

	describe('toggle functionality', () => {
		it('should toggle from light to dark on click', async () => {
			theme.set('light');
			render(ThemeToggle);

			const button = screen.getByRole('button');
			await fireEvent.click(button);

			expect(theme.current).toBe('dark');
		});

		it('should toggle from dark to light on click', async () => {
			theme.set('dark');
			render(ThemeToggle);

			const button = screen.getByRole('button');
			await fireEvent.click(button);

			expect(theme.current).toBe('light');
		});

		it('should toggle multiple times', async () => {
			theme.set('light');
			render(ThemeToggle);

			const button = screen.getByRole('button');

			await fireEvent.click(button);
			expect(theme.current).toBe('dark');

			await fireEvent.click(button);
			expect(theme.current).toBe('light');

			await fireEvent.click(button);
			expect(theme.current).toBe('dark');
		});
	});

	describe('styling', () => {
		it('should have transition-colors class for smooth transitions', () => {
			render(ThemeToggle);
			const button = screen.getByRole('button');
			expect(button.classList.contains('transition-colors')).toBe(true);
		});

		it('should have rounded-full class', () => {
			render(ThemeToggle);
			const button = screen.getByRole('button');
			expect(button.classList.contains('rounded-full')).toBe(true);
		});

		it('should have padding class', () => {
			render(ThemeToggle);
			const button = screen.getByRole('button');
			expect(button.classList.contains('p-2')).toBe(true);
		});
	});
});
