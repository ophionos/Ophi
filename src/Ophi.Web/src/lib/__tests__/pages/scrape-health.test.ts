import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/svelte';
import ScrapeHealthPage from '../../../routes/scrape-health/+page.svelte';
import type { ScrapeHealthSummary } from '$lib/api/client';
import type { PageData } from '../../../routes/scrape-health/$types';

// The page imports the api client for its Refresh button; nothing is called during mount,
// but the module must exist and not throw at import time.
vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			getScrapeHealth: vi.fn()
		}
	};
});

const emptySummary: ScrapeHealthSummary = {
	totalDomains: 0,
	totalScrapes7d: 0,
	overallSuccessRate: null,
	domainsHealthy: 0,
	domainsDegraded: 0,
	domainsUnhealthy: 0
};

function renderScrapeHealth(summary: Partial<ScrapeHealthSummary> = {}) {
	return render(ScrapeHealthPage, {
		props: {
			data: {
				user: { id: '1', email: 'a@b.com', name: 'A' },
				health: {
					domains: [],
					summary: { ...emptySummary, ...summary }
				}
			} as unknown as PageData
		}
	});
}

function successRateCard() {
	return screen.getByTestId('success-rate-card');
}

describe('Scrape Health Page — success rate card', () => {
	it('should show no data when no scrapes have ever run', () => {
		renderScrapeHealth();

		// The regression: an empty account reported a confident green "100%" about nothing.
		expect(within(successRateCard()).queryByText('100%')).not.toBeInTheDocument();
		expect(within(successRateCard()).getByText('No data')).toBeInTheDocument();
	});

	it('should not colour the card green when there is no data', () => {
		renderScrapeHealth();

		const value = within(successRateCard()).getByText('No data');
		expect(value.className).not.toMatch(/text-green/);
		expect(value.className).toMatch(/text-gray/);
	});

	it('should show 100% in green when every scrape succeeded', () => {
		renderScrapeHealth({ totalDomains: 1, totalScrapes7d: 12, overallSuccessRate: 1, domainsHealthy: 1 });

		const value = within(successRateCard()).getByText('100%');
		expect(value).toBeInTheDocument();
		expect(value.className).toMatch(/text-green/);
	});

	it('should show 0% in red when every scrape failed', () => {
		renderScrapeHealth({
			totalDomains: 1,
			totalScrapes7d: 4,
			overallSuccessRate: 0,
			domainsUnhealthy: 1
		});

		// A genuine zero is real data, not "no data".
		const value = within(successRateCard()).getByText('0%');
		expect(value).toBeInTheDocument();
		expect(value.className).toMatch(/text-red/);
	});

	it('should show an amber rate when the success rate is degraded', () => {
		renderScrapeHealth({
			totalDomains: 1,
			totalScrapes7d: 10,
			overallSuccessRate: 0.85,
			domainsDegraded: 1
		});

		const value = within(successRateCard()).getByText('85%');
		expect(value.className).toMatch(/text-amber/);
	});
});
