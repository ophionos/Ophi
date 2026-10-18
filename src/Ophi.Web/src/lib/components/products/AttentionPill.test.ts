import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import AttentionPill from './AttentionPill.svelte';
import type { ProductAttention } from '$lib/api/client';

vi.mock('$app/paths', () => ({ resolve: (path: string) => path }));

const attention = (overrides: Partial<ProductAttention>): ProductAttention => ({
	kind: 'blocked',
	isAction: true,
	productUrlId: 'u1',
	url: 'https://shop.example/x',
	consecutiveFailures: 1,
	...overrides
});

describe('AttentionPill', () => {
	it('should show an amber pill and a fix link for an action state', () => {
		render(AttentionPill, { props: { attention: attention({}), productId: 'p1' } });

		const pill = screen.getByText('Blocked');
		expect(pill.className).toContain('text-warning');
		expect(screen.getByTestId('attention-fix-link')).toHaveAttribute('href', '/products/p1');
		expect(screen.getByTestId('attention-fix-link')).toHaveTextContent('Fix');
	});

	it('should offer a review link for a price anomaly', () => {
		render(AttentionPill, {
			props: { attention: attention({ kind: 'priceAnomaly' }), productId: 'p1' }
		});

		expect(screen.getByText('Check price')).toBeInTheDocument();
		expect(screen.getByTestId('attention-fix-link')).toHaveTextContent('Review');
	});

	it('should show a grey pill and no link for an info state', () => {
		render(AttentionPill, {
			props: { attention: attention({ kind: 'paused', isAction: false }), productId: 'p1' }
		});

		expect(screen.getByText('Paused by you').className).not.toContain('text-warning');
		expect(screen.queryByTestId('attention-fix-link')).not.toBeInTheDocument();
	});
});
