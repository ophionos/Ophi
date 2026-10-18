import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/svelte';
import NeedsActionCard from './NeedsActionCard.svelte';
import type { AttentionKind, Product } from '$lib/api/client';

vi.mock('$app/paths', () => ({ resolve: (path: string) => path }));

function product(id: string, kind: AttentionKind, extra: Partial<Product> = {}): Product {
	return {
		id,
		name: `Product ${id}`,
		url: `https://www.shop.example/${id}`,
		currency: 'USD',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: [],
		attention: {
			kind,
			isAction: true,
			productUrlId: `url-${id}`,
			url: `https://www.shop.example/${id}`,
			consecutiveFailures: 3
		},
		...extra
	};
}

const baseProps = (products: Product[], extra = {}) => ({
	products,
	count: products.length,
	challengeAvailable: false,
	onRetry: vi.fn().mockResolvedValue(undefined),
	onSolveCheck: vi.fn(),
	onShowInTable: vi.fn(),
	...extra
});

beforeEach(() => localStorage.clear());

describe('NeedsActionCard', () => {
	it('should render nothing when nothing needs the user', () => {
		render(NeedsActionCard, { props: baseProps([]) });
		expect(screen.queryByTestId('needs-action-card')).not.toBeInTheDocument();
	});

	it('should show one line per problem with its cause, product and store', () => {
		render(NeedsActionCard, {
			props: baseProps([product('a', 'failing'), product('b', 'blocked')])
		});

		const items = screen.getAllByTestId('needs-action-item');
		expect(items).toHaveLength(2);
		expect(items[0]).toHaveTextContent('3 failed checks in a row');
		expect(items[0]).toHaveTextContent('Product a · shop.example');
		expect(screen.getByRole('heading', { name: /Needs your action · 2/ })).toBeInTheDocument();
	});

	it('should retry a failing URL from its line', async () => {
		const props = baseProps([product('a', 'failing')]);
		render(NeedsActionCard, { props });

		await fireEvent.click(screen.getByRole('button', { name: 'Retry now' }));

		expect(props.onRetry).toHaveBeenCalledWith(props.products[0]);
	});

	it('should offer the check for a blocked URL only when challenges are available', async () => {
		const props = baseProps([product('b', 'blocked')], { challengeAvailable: true });
		render(NeedsActionCard, { props });

		await fireEvent.click(screen.getByRole('button', { name: 'Solve check' }));

		expect(props.onSolveCheck).toHaveBeenCalledWith(props.products[0]);
		expect(screen.queryByRole('button', { name: 'Retry now' })).not.toBeInTheDocument();
	});

	it('should link an anomaly to the product page', () => {
		render(NeedsActionCard, { props: baseProps([product('c', 'priceAnomaly')]) });

		expect(screen.getByRole('link', { name: 'Review' })).toHaveAttribute('href', '/products/c');
	});

	it('should count the problems it does not list and filter the table on request', async () => {
		const props = baseProps([product('a', 'failing')], { count: 4 });
		render(NeedsActionCard, { props });

		expect(screen.getByTestId('needs-action-more')).toHaveTextContent('3 more');
		await fireEvent.click(screen.getByTestId('needs-action-show'));
		expect(props.onShowInTable).toHaveBeenCalledOnce();
	});

	it('should collapse and remember it', async () => {
		const { unmount } = render(NeedsActionCard, { props: baseProps([product('a', 'failing')]) });

		await fireEvent.click(screen.getByTestId('needs-action-toggle'));
		expect(screen.queryByTestId('needs-action-item')).not.toBeInTheDocument();
		expect(screen.getByTestId('needs-action-toggle')).toHaveAttribute('aria-expanded', 'false');
		unmount();

		render(NeedsActionCard, { props: baseProps([product('a', 'failing')]) });
		expect(
			within(screen.getByTestId('needs-action-card')).queryByTestId('needs-action-item')
		).not.toBeInTheDocument();
	});
});
