import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import TagBadge from './TagBadge.svelte';

describe('TagBadge', () => {
	it('should render the tag name', () => {
		render(TagBadge, { props: { name: 'Electronics', color: '#3b82f6' } });
		expect(screen.getByText('Electronics')).toBeInTheDocument();
	});

	it('should apply background color', () => {
		render(TagBadge, { props: { name: 'Test', color: '#ff0000' } });
		const badge = screen.getByTestId('tag-badge');
		expect(badge.style.backgroundColor).toBe('rgb(255, 0, 0)');
	});

	it('should use white text on dark background', () => {
		render(TagBadge, { props: { name: 'Test', color: '#000000' } });
		const badge = screen.getByTestId('tag-badge');
		expect(badge.style.color).toBe('rgb(255, 255, 255)');
	});

	it('should use black text on light background', () => {
		render(TagBadge, { props: { name: 'Test', color: '#ffffff' } });
		const badge = screen.getByTestId('tag-badge');
		expect(badge.style.color).toBe('rgb(0, 0, 0)');
	});

	it('should not render remove button by default', () => {
		render(TagBadge, { props: { name: 'Test', color: '#3b82f6' } });
		expect(screen.queryByRole('button')).not.toBeInTheDocument();
	});

	it('should render remove button when removable', () => {
		const onRemove = vi.fn();
		render(TagBadge, { props: { name: 'Test', color: '#3b82f6', removable: true, onRemove } });
		expect(screen.getByRole('button', { name: /Remove tag Test/ })).toBeInTheDocument();
	});

	it('should call onRemove when remove button clicked', async () => {
		const onRemove = vi.fn();
		render(TagBadge, { props: { name: 'Test', color: '#3b82f6', removable: true, onRemove } });

		await fireEvent.click(screen.getByRole('button', { name: /Remove tag Test/ }));

		expect(onRemove).toHaveBeenCalledOnce();
	});
});
