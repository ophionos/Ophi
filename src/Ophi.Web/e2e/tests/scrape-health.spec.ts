import { test, expect } from '../fixtures/auth.fixture';
import { ScrapeHealthPage } from '../pages/scrape-health.page';

test.describe('Scrape Health', () => {
	let scrapeHealth: ScrapeHealthPage;

	test.beforeEach(({ page }) => {
		scrapeHealth = new ScrapeHealthPage(page);
	});

	test('should render the dashboard with summary cards', async () => {
		await scrapeHealth.goto();

		await expect(scrapeHealth.heading).toBeVisible();
		await expect(scrapeHealth.refreshButton).toBeVisible();
		await expect(scrapeHealth.statCard('Domains')).toBeVisible();
		await expect(scrapeHealth.statCard('Scrapes (7d)')).toBeVisible();
		await expect(scrapeHealth.statCard('Success Rate')).toBeVisible();
		await expect(scrapeHealth.statCard('Status')).toBeVisible();
	});

	test('should reload data via the Refresh button without error', async () => {
		await scrapeHealth.goto();
		await scrapeHealth.refresh();

		await expect(scrapeHealth.errorBox).not.toBeVisible();
		await expect(scrapeHealth.statCard('Domains')).toBeVisible();
	});

	test('should show the empty state when there is no scrape data', async () => {
		await scrapeHealth.goto();
		// The dev API has no scrape activity (worker disabled), so the per-domain
		// table renders its empty state.
		await expect(scrapeHealth.emptyState).toBeVisible();
	});
});
