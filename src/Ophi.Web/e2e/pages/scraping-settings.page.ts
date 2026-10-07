import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Page Object for /settings/scraping. Every control persists on change via
 * PUT /settings. Controls are one-way bound (`value={...}`) and an on-mount
 * `$effect` re-syncs them from loader data, so interactions are wrapped to
 * survive the hydration window (see fillNumber / the commit retries).
 */
export class ScrapingSettingsPage extends BasePage {
	readonly affiliatesToggle: Locator;
	readonly checkInterval: Locator;
	readonly pageFetchDelay: Locator;
	readonly scrapeCacheTtl: Locator;
	readonly anomalyThreshold: Locator;
	readonly autoPauseFailures: Locator;

	constructor(page: Page) {
		super(page);
		this.affiliatesToggle = page.getByTestId('affiliates-toggle');
		this.checkInterval = page.getByTestId('default-check-interval');
		this.pageFetchDelay = page.getByTestId('page-fetch-delay');
		this.scrapeCacheTtl = page.getByTestId('scrape-cache-ttl');
		this.anomalyThreshold = page.getByTestId('anomaly-threshold');
		this.autoPauseFailures = page.getByTestId('auto-pause-failures');
	}

	async goto() {
		await this.page.goto('/settings/scraping', { waitUntil: 'domcontentloaded' });
		await expect(this.affiliatesToggle).toBeVisible();
	}

	private settingsPut(timeout: number) {
		return this.page.waitForResponse(
			(r) => r.url().includes('/api/v1/settings') && r.request().method() === 'PUT',
			{ timeout }
		);
	}

	/** Runs an action and waits for the persist PUT, retrying past the hydration window. */
	private async commit(action: () => Promise<void>) {
		await expect(async () => {
			const pending = this.settingsPut(3000);
			await action();
			await pending;
		}).toPass({ timeout: 15000 });
	}

	async toggleAffiliates() {
		await this.commit(() => this.affiliatesToggle.click());
	}

	async selectInterval(value: string) {
		await this.commit(() => this.checkInterval.selectOption(value));
	}

	/** Fills a number field so the value survives the on-mount $effect, then commits on blur. */
	async setNumber(field: Locator, value: number) {
		const v = String(value);
		await expect(async () => {
			await field.fill(v);
			// Let the hydration $effect re-sync (and clobber) fire before committing.
			await this.page.waitForTimeout(300);
			await expect(field).toHaveValue(v, { timeout: 500 });
			const pending = this.settingsPut(3000);
			await field.blur();
			await pending;
		}).toPass({ timeout: 20000 });
	}
}
