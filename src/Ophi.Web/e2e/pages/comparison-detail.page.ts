import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class ComparisonDetailPage extends BasePage {
	readonly backLink: Locator;
	readonly groupName: Locator;
	readonly loadingSpinner: Locator;
	readonly addProductButton: Locator;
	readonly deleteGroupButton: Locator;

	// Winner spotlight
	readonly winnerSpotlight: Locator;

	// Chart
	readonly chartSection: Locator;
	readonly dayButtons: Locator;

	// Products list
	readonly productsSection: Locator;
	readonly productRows: Locator;
	readonly emptyProducts: Locator;

	constructor(page: Page) {
		super(page);
		this.backLink = page.getByRole('link', { name: 'Back to Comparisons' });
		this.groupName = page.locator('h1');
		this.loadingSpinner = page.locator('[data-testid="page-loading"]');
		this.addProductButton = page.getByRole('button', { name: 'Add Product' });
		this.deleteGroupButton = page.getByRole('button', { name: 'Delete Group' });

		// Winner spotlight
		this.winnerSpotlight = page.locator('[data-testid="winner-spotlight"]');

		// Chart section
		this.chartSection = page.locator('div').filter({
			has: page.getByRole('heading', { name: 'Price Comparison' })
		}).first();
		this.dayButtons = this.chartSection.locator('button');

		// Products
		this.productsSection = page.locator('div').filter({
			has: page.getByRole('heading', { name: /^Products \(/ })
		}).first();
		this.productRows = this.productsSection.locator('.divide-y > div');
		this.emptyProducts = page.getByText('No products in this group');
	}

	async goto(groupId: string) {
		await this.page.goto(`/comparisons/${groupId}`);
		await this.waitForPageLoad();
	}

	async waitForLoaded() {
		await expect(this.loadingSpinner).not.toBeVisible({ timeout: 10000 });
		await expect(this.groupName).toBeVisible({ timeout: 10000 });
	}

	async clickBack() {
		await this.backLink.click();
	}

	async clickAddProduct() {
		await this.addProductButton.click();
	}

	async clickDeleteGroup() {
		await this.deleteGroupButton.click();
	}

	async selectChartDays(days: 7 | 30 | 90) {
		await this.chartSection.getByRole('button', { name: `${days}d` }).click();
	}

	async clickRemoveProduct(productName: string) {
		const row = this.productRows.filter({ hasText: productName });
		await row.getByRole('button', { name: 'Remove from group' }).click();
	}

	async getProductCount(): Promise<number> {
		return await this.productRows.count();
	}

	async expectProductVisible(productName: string) {
		await expect(this.productRows.filter({ hasText: productName })).toBeVisible();
	}

	async expectProductNotVisible(productName: string) {
		await expect(this.productRows.filter({ hasText: productName })).not.toBeVisible();
	}

	async expectGroupName(name: string) {
		await expect(this.groupName).toContainText(name);
	}

	async expectWinnerVisible(productName: string) {
		await expect(this.winnerSpotlight).toBeVisible();
		await expect(this.winnerSpotlight).toContainText(productName);
	}

	async expectNoWinner() {
		await expect(this.winnerSpotlight).not.toBeVisible();
	}

	async expectEmptyProducts() {
		await expect(this.emptyProducts).toBeVisible();
	}
}
