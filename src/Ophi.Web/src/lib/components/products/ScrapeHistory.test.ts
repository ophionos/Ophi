import { beforeEach, describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ScrapeHistory from './ScrapeHistory.svelte';

vi.mock('$lib/api/client', () => ({
	api: {
		getScrapeLog: vi.fn()
	}
}));

import { api } from '$lib/api/client';
const mockGetScrapeLog = vi.mocked(api.getScrapeLog);

describe('ScrapeHistory', () => {
	const defaultProps = {
		productId: 'product-1',
		currency: 'USD'
	};

	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('collapsed state', () => {
		it('should render collapsed by default', () => {
			render(ScrapeHistory, { props: defaultProps });
			expect(screen.getByText('Scrape History')).toBeInTheDocument();
			expect(screen.queryByTestId('scrape-loading')).not.toBeInTheDocument();
		});

		it('should not load data when collapsed', () => {
			render(ScrapeHistory, { props: defaultProps });
			expect(mockGetScrapeLog).not.toHaveBeenCalled();
		});
	});

	describe('expanding', () => {
		it('should load data on expand', async () => {
			mockGetScrapeLog.mockResolvedValue({
				items: [
					{
						id: '1',
						success: true,
						price: 99.99,
						durationMs: 150,
						url: 'https://example.com/product',
						isOutOfStock: false,
						storeDomain: 'example.com',
						createdAt: '2024-01-15T10:00:00Z'
					}
				]
			});

			render(ScrapeHistory, { props: defaultProps });
			const header = screen.getByText('Scrape History');
			await fireEvent.click(header);

			expect(mockGetScrapeLog).toHaveBeenCalledWith('product-1', 20);
		});

		it('should show loading state while fetching', async () => {
			mockGetScrapeLog.mockImplementation(
				() => new Promise((resolve) => setTimeout(() => resolve({ items: [] }), 100))
			);

			render(ScrapeHistory, { props: defaultProps });
			const header = screen.getByText('Scrape History');
			await fireEvent.click(header);

			expect(screen.getByTestId('scrape-loading')).toBeInTheDocument();
		});
	});

	describe('rendering entries', () => {
		it('should render success entries with price', async () => {
			mockGetScrapeLog.mockResolvedValue({
				items: [
					{
						id: '1',
						success: true,
						price: 49.99,
						durationMs: 250,
						url: 'https://example.com/product',
						isOutOfStock: false,
						storeDomain: 'example.com',
						createdAt: '2024-01-15T10:00:00Z'
					}
				]
			});

			render(ScrapeHistory, { props: defaultProps });
			await fireEvent.click(screen.getByText('Scrape History'));

			await waitFor(() => {
				expect(screen.getByText('$49.99')).toBeInTheDocument();
				expect(screen.getByText('250ms')).toBeInTheDocument();
			});
		});

		it('should render error entries', async () => {
			mockGetScrapeLog.mockResolvedValue({
				items: [
					{
						id: '2',
						success: false,
						error: 'Failed to extract price',
						durationMs: 500,
						url: 'https://example.com/product',
						isOutOfStock: false,
						storeDomain: 'example.com',
						createdAt: '2024-01-15T10:00:00Z'
					}
				]
			});

			render(ScrapeHistory, { props: defaultProps });
			await fireEvent.click(screen.getByText('Scrape History'));

			await waitFor(() => {
				expect(screen.getByText('Failed to extract price')).toBeInTheDocument();
			});
		});

		it('should show empty message when no entries', async () => {
			mockGetScrapeLog.mockResolvedValue({ items: [] });

			render(ScrapeHistory, { props: defaultProps });
			await fireEvent.click(screen.getByText('Scrape History'));

			await waitFor(() => {
				expect(screen.getByText('No scrape history available')).toBeInTheDocument();
			});
		});

		it('should format duration in seconds for values >= 1000ms', async () => {
			mockGetScrapeLog.mockResolvedValue({
				items: [
					{
						id: '3',
						success: true,
						price: 10,
						durationMs: 2500,
						isOutOfStock: false,
						storeDomain: 'example.com',
						createdAt: '2024-01-15T10:00:00Z'
					}
				]
			});

			render(ScrapeHistory, { props: defaultProps });
			await fireEvent.click(screen.getByText('Scrape History'));

			await waitFor(() => {
				expect(screen.getByText('2.5s')).toBeInTheDocument();
			});
		});

		it('should show domain from URL', async () => {
			mockGetScrapeLog.mockResolvedValue({
				items: [
					{
						id: '4',
						success: true,
						price: 10,
						durationMs: 100,
						url: 'https://www.amazon.com/product/123',
						isOutOfStock: false,
						storeDomain: 'www.amazon.com',
						createdAt: '2024-01-15T10:00:00Z'
					}
				]
			});

			render(ScrapeHistory, { props: defaultProps });
			await fireEvent.click(screen.getByText('Scrape History'));

			await waitFor(() => {
				expect(screen.getByText('www.amazon.com')).toBeInTheDocument();
			});
		});
	});
});
