import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ProductUrlCard from './ProductUrlCard.svelte';
import type { ProductUrl } from '$lib/api/client';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

describe('ProductUrlCard', () => {
	const mockProductUrl: ProductUrl = {
		id: 'url-1',
		url: 'https://amazon.com/dp/B12345',
		currentPrice: 29.99,
		currency: 'USD',
		lastCheckedAt: '2026-01-15T10:00:00Z',
		failureCount: 0,
		status: 'active',
		isOutOfStock: false
	};

	const defaultProps = {
		productUrl: mockProductUrl,
		canDelete: true,
		onDelete: vi.fn()
	};

	describe('rendering', () => {
		it('should display the domain name', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.getByText('amazon.com')).toBeInTheDocument();
		});

		it('should display current price', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.getByText('$29.99')).toBeInTheDocument();
		});

		it('should show "No price yet" when no current price', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: { ...mockProductUrl, currentPrice: undefined }
				}
			});
			expect(screen.getByText('No price yet')).toBeInTheDocument();
		});

		it('should show error indicator when lastError is set', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: { ...mockProductUrl, lastError: 'Timeout' }
				}
			});
			expect(screen.getByText('Error')).toBeInTheDocument();
		});

		it('should render external link', () => {
			render(ProductUrlCard, { props: defaultProps });
			const link = screen.getByTitle('Open in new tab');
			expect(link).toHaveAttribute('href', 'https://amazon.com/dp/B12345');
		});

		it('should render store configuration link with domain', () => {
			render(ProductUrlCard, { props: defaultProps });
			const link = screen.getByTitle('Store configuration');
			expect(link).toHaveAttribute('href', '/stores?edit=amazon.com');
		});

		it('should hide the store configuration link for a built-in store', () => {
			render(ProductUrlCard, { props: { ...defaultProps, isBuiltInStore: true } });
			expect(screen.queryByTitle('Store configuration')).not.toBeInTheDocument();
		});

		it('should show the store configuration link for a non-built-in store', () => {
			render(ProductUrlCard, { props: { ...defaultProps, isBuiltInStore: false } });
			expect(screen.getByTitle('Store configuration')).toBeInTheDocument();
		});

		it('should show delete button when canDelete is true', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.getByTitle('Remove URL')).toBeInTheDocument();
		});

		it('should hide delete button when canDelete is false', () => {
			render(ProductUrlCard, { props: { ...defaultProps, canDelete: false } });
			expect(screen.queryByTitle('Remove URL')).not.toBeInTheDocument();
		});
	});

	describe('interactions', () => {
		it('should call onDelete when delete button is clicked', async () => {
			const onDelete = vi.fn();
			render(ProductUrlCard, { props: { ...defaultProps, onDelete } });

			await fireEvent.click(screen.getByTitle('Remove URL'));
			expect(onDelete).toHaveBeenCalledWith('url-1');
		});

		it('should call onRetry when retry button is clicked', async () => {
			const onRetry = vi.fn();
			render(ProductUrlCard, { props: { ...defaultProps, onRetry } });

			await fireEvent.click(screen.getByTestId('retry-scrape-button'));
			expect(onRetry).toHaveBeenCalledWith('url-1');
		});

		it('should disable retry button during cooldown', async () => {
			const onRetry = vi.fn();
			render(ProductUrlCard, { props: { ...defaultProps, onRetry } });

			await fireEvent.click(screen.getByTestId('retry-scrape-button'));

			const button = screen.getByTestId('retry-scrape-button');
			expect(button).toBeDisabled();
		});

		it('should show retry button only when onRetry is provided', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.queryByTestId('retry-scrape-button')).not.toBeInTheDocument();
		});

		it('should show retry button when onRetry is provided', () => {
			const onRetry = vi.fn();
			render(ProductUrlCard, { props: { ...defaultProps, onRetry } });
			expect(screen.getByTestId('retry-scrape-button')).toBeInTheDocument();
		});
	});

	describe('url health monitoring', () => {
		it('should show suspicious badge when status is suspicious', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'suspicious',
						suspiciousReason: 'Redirected to different domain'
					}
				}
			});
			expect(screen.getByTestId('suspicious-badge')).toBeInTheDocument();
			expect(screen.getByText('Suspicious')).toBeInTheDocument();
		});

		it('should show paused badge when status is paused', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'paused',
						suspiciousReason: 'Auto-paused after 3 suspicious scrapes'
					}
				}
			});
			expect(screen.getByTestId('paused-badge')).toBeInTheDocument();
			expect(screen.getByText('Paused')).toBeInTheDocument();
		});

		it('should show resume button when status is paused and onResume is provided', () => {
			const onResume = vi.fn();
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					onResume,
					productUrl: { ...mockProductUrl, status: 'paused' }
				}
			});
			expect(screen.getByTestId('resume-url-button')).toBeInTheDocument();
		});

		it('should not show resume button when status is active', () => {
			const onResume = vi.fn();
			render(ProductUrlCard, {
				props: { ...defaultProps, onResume }
			});
			expect(screen.queryByTestId('resume-url-button')).not.toBeInTheDocument();
		});

		it('should call onResume when resume button is clicked', async () => {
			const onResume = vi.fn();
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					onResume,
					productUrl: { ...mockProductUrl, status: 'paused' }
				}
			});

			await fireEvent.click(screen.getByTestId('resume-url-button'));
			expect(onResume).toHaveBeenCalledWith('url-1');
		});

		it('should not show error indicator when status is paused', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'paused',
						lastError: 'Some error'
					}
				}
			});
			expect(screen.queryByText('Error')).not.toBeInTheDocument();
		});

		it('should not show suspicious or paused badge when status is active', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.queryByTestId('suspicious-badge')).not.toBeInTheDocument();
			expect(screen.queryByTestId('paused-badge')).not.toBeInTheDocument();
		});

		it('should show out of stock badge when URL is out of stock', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: { ...mockProductUrl, isOutOfStock: true }
				}
			});
			expect(screen.getByTestId('out-of-stock-badge')).toBeInTheDocument();
			expect(screen.getByTestId('out-of-stock-badge').textContent).toContain('Out of Stock');
		});

		it('should not show out of stock badge when URL is in stock', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.queryByTestId('out-of-stock-badge')).not.toBeInTheDocument();
		});
	});

	describe('failure explanation', () => {
		it('should display the pause reason as visible text when status is paused', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'paused',
						suspiciousReason: 'Auto-paused after 3 suspicious scrapes'
					}
				}
			});
			expect(screen.getByTestId('url-failure-reason')).toHaveTextContent(
				'Auto-paused after 3 suspicious scrapes'
			);
		});

		it('should fall back to lastError when a paused URL has no suspiciousReason', () => {
			// The anti-bot auto-pause path (CheckProductPriceHandler) pauses WITHOUT setting
			// SuspiciousReason, so lastError is the only thing that explains the pause.
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'paused',
						lastError: 'Blocked by anti-bot protection'
					}
				}
			});
			expect(screen.getByTestId('url-failure-reason')).toHaveTextContent(
				'Blocked by anti-bot protection'
			);
		});

		it('should explain a network block without blaming the user', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'paused',
						lastError: 'Blocked by anti-bot protection'
					}
				}
			});
			expect(screen.getByTestId('url-blocked-hint')).toBeInTheDocument();
			expect(screen.getByTestId('url-blocked-hint').textContent).toContain("server's network");
		});

		it('should not describe a pause as suspicious user activity', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: { ...mockProductUrl, status: 'paused' }
				}
			});
			expect(screen.getByTestId('url-failure-reason').textContent).not.toContain(
				'suspicious activity'
			);
		});

		it('should display lastError as visible text for a non-paused failure', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: { ...mockProductUrl, lastError: 'Page not found (HTTP 404)' }
				}
			});
			expect(screen.getByTestId('url-failure-reason')).toHaveTextContent(
				'Page not found (HTTP 404)'
			);
		});

		it('should show the suspicious reason as visible text', () => {
			render(ProductUrlCard, {
				props: {
					...defaultProps,
					productUrl: {
						...mockProductUrl,
						status: 'suspicious',
						suspiciousReason: 'Redirected to different domain'
					}
				}
			});
			expect(screen.getByTestId('url-failure-reason')).toHaveTextContent(
				'Redirected to different domain'
			);
		});

		it('should not render a failure reason for a healthy URL', () => {
			render(ProductUrlCard, { props: defaultProps });
			expect(screen.queryByTestId('url-failure-reason')).not.toBeInTheDocument();
			expect(screen.queryByTestId('url-blocked-hint')).not.toBeInTheDocument();
		});
	});
});
