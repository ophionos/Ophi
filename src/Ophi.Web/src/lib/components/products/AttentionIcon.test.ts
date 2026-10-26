import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import AttentionIcon from './AttentionIcon.svelte';
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

describe('AttentionIcon', () => {
	it('should link to the product with the attention label when there is a problem to fix', () => {
		render(AttentionIcon, { props: { attention: attention({}), productId: 'p1' } });
		const icon = screen.getByTestId('attention-icon');

		expect(icon).toHaveAttribute('href', '/products/p1');
		expect(icon).toHaveAttribute('title', 'Blocked');
		expect(icon).toHaveAttribute('aria-label', 'Blocked');
		expect(icon).toHaveAttribute('data-kind', 'blocked');
		expect(icon.className).toContain('text-warning');
	});

	it('should use a gray icon when the state is information only', () => {
		render(AttentionIcon, {
			props: { attention: attention({ kind: 'paused', isAction: false }), productId: 'p1' }
		});
		const icon = screen.getByTestId('attention-icon');

		expect(icon).toHaveAttribute('data-kind', 'paused');
		expect(icon).toHaveAttribute('aria-label', 'Paused by you');
		expect(icon.className).not.toContain('text-warning');
	});
});
