import { Page, Locator, Download, expect } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Page Object for /settings/data (product CSV/JSON import & export).
 * Interactions are wrapped in toPass retries because the click/change handlers
 * are only live once the page has hydrated.
 */
export class DataSettingsPage extends BasePage {
	readonly exportCsvButton: Locator;
	readonly exportJsonButton: Locator;
	readonly importButton: Locator;
	readonly fileInput: Locator;

	constructor(page: Page) {
		super(page);
		this.exportCsvButton = page.getByRole('button', { name: 'Export CSV' });
		this.exportJsonButton = page.getByRole('button', { name: 'Export JSON' });
		this.importButton = page.getByRole('button', { name: 'Import CSV' });
		this.fileInput = page.locator('input[type="file"][accept=".csv"]'); // the backup card has its own file input
	}

	async goto() {
		await this.page.goto('/settings/data', { waitUntil: 'domcontentloaded' });
		await expect(this.exportCsvButton).toBeVisible();
	}

	/** Clicks an export button and returns the triggered download (retried for hydration). */
	private async export(button: Locator): Promise<Download> {
		let download: Download | undefined;
		await expect(async () => {
			const pending = this.page.waitForEvent('download', { timeout: 2000 });
			await button.click();
			download = await pending;
		}).toPass({ timeout: 15000 });
		return download!;
	}

	exportCsv() {
		return this.export(this.exportCsvButton);
	}

	exportJson() {
		return this.export(this.exportJsonButton);
	}

	/** Uploads a CSV via the hidden file input and returns the import API response. */
	async importCsv(content: string) {
		let response: import('@playwright/test').Response | undefined;
		await expect(async () => {
			const pending = this.page.waitForResponse(
				(r) => r.url().includes('/api/v1/products/import') && r.request().method() === 'POST',
				{ timeout: 3000 }
			);
			await this.fileInput.setInputFiles({
				name: 'import.csv',
				mimeType: 'text/csv',
				buffer: Buffer.from(content)
			});
			response = await pending;
		}).toPass({ timeout: 20000 });
		return response!;
	}
}
