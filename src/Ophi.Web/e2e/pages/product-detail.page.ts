import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class ProductDetailPage extends BasePage {
	readonly backButton: Locator;
	readonly productName: Locator;
	readonly viewOnStoreLink: Locator;
	readonly statusBadge: Locator;
	readonly currentPrice: Locator;
	readonly priceChangeIndicator: Locator;
	readonly productImage: Locator;

	// Statistics
	readonly lowestPrice: Locator;
	readonly highestPrice: Locator;
	readonly averagePrice: Locator;
	readonly currentStatPrice: Locator;

	// Chart
	readonly priceChartSection: Locator;
	readonly chartDayButtons: Locator;

	// Alerts
	readonly alertsSection: Locator;
	readonly createAlertButton: Locator;
	readonly alertItems: Locator;

	// Comparison group
	readonly comparisonSection: Locator;
	readonly groupSelect: Locator;
	readonly removeFromGroupButton: Locator;

	// Danger zone
	readonly dangerZone: Locator;
	readonly deleteProductButton: Locator;

	readonly loadingSpinner: Locator;
	readonly errorMessage: Locator;

	constructor(page: Page) {
		super(page);
		this.backButton = page.locator('[data-testid="breadcrumbs"]').getByRole('link', { name: 'Dashboard' });
		this.productName = page.getByRole('heading', { level: 1 });
		this.viewOnStoreLink = page.getByText('View on store');
		this.statusBadge = page.locator('span.rounded-full.capitalize');
		this.currentPrice = page.locator('[data-testid="single-price-display"] .text-3xl');
		this.priceChangeIndicator = page.locator('.text-lg').filter({ has: page.locator('svg') });
		this.productImage = page.locator('img[alt]').first();

		// Statistics - find by label text within stat cards
		const statsGrid = page.locator('.grid.grid-cols-2');
		this.lowestPrice = statsGrid
			.locator('div')
			.filter({ hasText: 'Lowest' })
			.locator('p.text-xl');
		this.highestPrice = statsGrid
			.locator('div')
			.filter({ hasText: 'Highest' })
			.locator('p.text-xl');
		this.averagePrice = statsGrid
			.locator('div')
			.filter({ hasText: 'Average' })
			.locator('p.text-xl');
		this.currentStatPrice = statsGrid
			.locator('div')
			.filter({ hasText: 'Current' })
			.locator('p.text-xl');

		// Chart - use heading to uniquely identify the section
		this.priceChartSection = page.locator('div').filter({
			has: page.getByRole('heading', { name: 'Price History', exact: true })
		}).first();
		this.chartDayButtons = this.priceChartSection.locator('button');

		// Alerts - use heading to uniquely identify the section
		this.alertsSection = page.locator('div').filter({
			has: page.getByRole('heading', { name: 'Price Alerts' })
		}).first();
		this.createAlertButton = this.alertsSection.getByRole('button', { name: /Create Alert/i });
		this.alertItems = this.alertsSection.locator('.bg-surface-2.rounded-lg');

		// Comparison - use heading to uniquely identify the section
		this.comparisonSection = page.locator('div').filter({
			has: page.getByRole('heading', { name: 'Comparison Group', exact: true })
		}).first();
		this.groupSelect = this.comparisonSection.locator('select');
		this.removeFromGroupButton = this.comparisonSection.getByRole('button', { name: 'Remove' });

		// Danger zone - collapsible section
		this.dangerZone = page.locator('[data-testid="danger-zone"]');
		this.deleteProductButton = page.getByRole('button', { name: 'Delete Product' });

		this.loadingSpinner = page.locator('[data-testid="page-loading"]');
		this.errorMessage = page.locator('.text-red-400').locator('..');
	}

	async goto(productId: string) {
		await this.page.goto(`/products/${productId}`);
		await this.waitForPageLoad();
	}

	async waitForProductLoaded() {
		await expect(this.loadingSpinner).not.toBeVisible({ timeout: 10000 });
		await expect(this.productName).toBeVisible({ timeout: 10000 });
	}

	/**
	 * Wait for a product to leave pending status by polling the API.
	 * Use this after creating a product to wait for background scraping to complete.
	 */
	async waitForProductReady(productId: string, maxWaitMs: number = 30000) {
		const startTime = Date.now();
		const pollInterval = 500;

		while (Date.now() - startTime < maxWaitMs) {
			try {
				const response = await this.page.request.get(
					`/api/v1/products/${productId}`
				);
				if (response.ok()) {
					const product = await response.json();
					if (product.status !== 'pending') {
						return product;
					}
				}
			} catch {
				// Ignore errors and keep polling
			}
			await this.page.waitForTimeout(pollInterval);
		}

		throw new Error(`Product ${productId} did not leave pending status within ${maxWaitMs}ms`);
	}

	async clickBack() {
		await this.backButton.click();
	}

	async selectChartDays(days: 7 | 30 | 90) {
		await this.priceChartSection.getByRole('button', { name: `${days}d` }).click();
	}

	async clickCreateAlert() {
		await this.createAlertButton.click();
	}

	async deleteAlertByIndex(index: number) {
		const alertItem = this.alertItems.nth(index);
		await alertItem.getByRole('button', { name: 'Delete alert' }).click();
	}

	async addToComparisonGroup(groupName: string) {
		await this.groupSelect.selectOption({ label: groupName });
	}

	async clickRemoveFromGroup() {
		await this.removeFromGroupButton.click();
	}

	async openDangerZone() {
		// Open the collapsible danger zone if not already open
		const deleteBtn = this.deleteProductButton;
		if (!(await deleteBtn.isVisible())) {
			await this.dangerZone.locator('button').first().click();
			await expect(deleteBtn).toBeVisible({ timeout: 5000 });
		}
	}

	async clickDeleteProduct() {
		await this.openDangerZone();
		await this.deleteProductButton.click();
	}

	async getAlertCount(): Promise<number> {
		return await this.alertItems.count();
	}

	async expectProductName(name: string) {
		await expect(this.productName).toContainText(name);
	}

	async expectPrice(currency: string, amount: string) {
		await expect(this.currentPrice).toContainText(`${currency} ${amount}`);
	}

	async expectStatus(status: string) {
		await expect(this.statusBadge).toContainText(status);
	}

	async expectStatistics(lowest: string, highest: string, average: string, current: string) {
		await expect(this.lowestPrice).toContainText(lowest);
		await expect(this.highestPrice).toContainText(highest);
		await expect(this.averagePrice).toContainText(average);
		await expect(this.currentStatPrice).toContainText(current);
	}

	async expectAlertVisible(targetPrice: string, condition: string) {
		const alert = this.alertItems.filter({ hasText: targetPrice }).filter({ hasText: condition });
		await expect(alert).toBeVisible();
	}

	async expectNoAlerts() {
		await expect(this.alertItems).toHaveCount(0);
		await expect(this.alertsSection.getByText('No alerts set for this product')).toBeVisible();
	}
}
