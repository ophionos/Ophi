import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import ScrapeHealthTable from './ScrapeHealthTable.svelte';
import type { DomainHealth } from '$lib/api/client';

describe('ScrapeHealthTable', () => {
	const healthyDomain: DomainHealth = {
		domain: 'amazon.com',
		totalScrapes: 100,
		successCount: 98,
		failureCount: 2,
		successRate: 0.98,
		scrapesToday: 5,
		p50DurationMs: 450,
		p95DurationMs: 1200,
		lastFailureMessage: undefined,
		lastFailureAt: undefined
	};

	const degradedDomain: DomainHealth = {
		domain: 'ebay.com',
		totalScrapes: 50,
		successCount: 42,
		failureCount: 8,
		successRate: 0.85,
		scrapesToday: 3,
		p50DurationMs: 800,
		p95DurationMs: 3500,
		lastFailureMessage: 'Timeout after 30s',
		lastFailureAt: '2026-04-02T12:00:00Z'
	};

	const unhealthyDomain: DomainHealth = {
		domain: 'broken-store.com',
		totalScrapes: 20,
		successCount: 10,
		failureCount: 10,
		successRate: 0.5,
		scrapesToday: 1,
		p50DurationMs: 2000,
		p95DurationMs: 5000,
		lastFailureMessage: 'HTTP 403 Forbidden',
		lastFailureAt: '2026-04-03T08:00:00Z'
	};

	it('should show empty state when no domains', () => {
		render(ScrapeHealthTable, { props: { domains: [] } });

		expect(screen.getByText('No scrape data yet')).toBeInTheDocument();
		expect(screen.getByRole('link', { name: 'Go to dashboard' })).toHaveAttribute('href', '/dashboard');
	});

	it('should render domain names', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain, degradedDomain] } });

		expect(screen.getAllByText('amazon.com').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('ebay.com').length).toBeGreaterThanOrEqual(1);
	});

	it('should render success rate badges', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain, degradedDomain, unhealthyDomain] } });

		expect(screen.getAllByText('98%').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('85%').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('50%').length).toBeGreaterThanOrEqual(1);
	});

	it('should render scrape counts', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain] } });

		expect(screen.getAllByText('100').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('5').length).toBeGreaterThanOrEqual(1);
	});

	it('should format durations correctly', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain] } });

		// p50 = 450ms, p95 = 1200ms = 1.2s
		expect(screen.getAllByText('450ms').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('1.2s').length).toBeGreaterThanOrEqual(1);
	});

	it('should show last failure message when present', () => {
		render(ScrapeHealthTable, { props: { domains: [degradedDomain] } });

		expect(screen.getAllByText('Timeout after 30s').length).toBeGreaterThanOrEqual(1);
	});

	it('should show "--" for last error when no failures', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain] } });

		expect(screen.getAllByText('--').length).toBeGreaterThanOrEqual(1);
	});

	it('should render the desktop table', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain] } });

		expect(screen.getByTestId('scrape-health-table')).toBeInTheDocument();
	});

	it('should render multiple domains', () => {
		render(ScrapeHealthTable, { props: { domains: [healthyDomain, degradedDomain, unhealthyDomain] } });

		expect(screen.getAllByText('amazon.com').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('ebay.com').length).toBeGreaterThanOrEqual(1);
		expect(screen.getAllByText('broken-store.com').length).toBeGreaterThanOrEqual(1);
	});
});
