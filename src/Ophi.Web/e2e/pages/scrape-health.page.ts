import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

/** Page Object for /scrape-health — a read-only per-domain scrape dashboard. */
export class ScrapeHealthPage extends BasePage {
	readonly heading: Locator;
	readonly refreshButton: Locator;
	readonly emptyState: Locator;
	readonly errorBox: Locator;

	constructor(page: Page) {
		super(page);
		this.heading = page.getByRole('heading', { name: 'Scrape Health' });
		this.refreshButton = page.getByRole('button', { name: 'Refresh' });
		this.emptyState = page.getByText('No scrape data yet');
		this.errorBox = page.getByText('Failed to load scrape health');
	}

	async goto() {
		await this.page.goto('/scrape-health', { waitUntil: 'domcontentloaded' });
		await expect(this.heading).toBeVisible();
	}

	statCard(label: string): Locator {
		return this.page.getByText(label, { exact: true });
	}

	async refresh() {
		await expect(async () => {
			const pending = this.page.waitForResponse(
				(r) => r.url().includes('/api/v1/scrape-health') && r.request().method() === 'GET',
				{ timeout: 3000 }
			);
			await this.refreshButton.click();
			await pending;
		}).toPass({ timeout: 15000 });
	}
}
