import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class StoresPage extends BasePage {
	readonly pageTitle: Locator;
	readonly addStoreButton: Locator;
	readonly storeCards: Locator;
	readonly loadingSpinner: Locator;
	readonly emptyState: Locator;
	readonly totalStoresCount: Locator;
	readonly builtInStoresCount: Locator;
	readonly customStoresCount: Locator;

	constructor(page: Page) {
		super(page);
		this.pageTitle = page.getByRole('heading', { name: 'Store Configurations' });
		this.addStoreButton = page.getByRole('button', { name: 'Add Store' });
		this.storeCards = page.getByTestId('store-card');
		this.loadingSpinner = page.locator('[data-testid="page-loading"]');
		this.emptyState = page.getByText('No stores configured');
		// The counts live on the filter chips ("All (n)", "Built-in (n)", …).
		this.totalStoresCount = page.getByTestId('store-filter-all');
		this.builtInStoresCount = page.getByTestId('store-filter-built-in');
		this.customStoresCount = page.getByTestId('store-filter-custom');
	}

	async goto() {
		await this.page.goto('/stores');
		await this.waitForPageLoad();
	}

	async waitForStoresLoaded() {
		await expect(this.loadingSpinner).not.toBeVisible({ timeout: 10000 });
	}

	async clickAddStore() {
		await this.addStoreButton.click();
	}

	getStoreCardByName(name: string): Locator {
		return this.storeCards.filter({ hasText: name });
	}

	async clickEditStore(storeName: string) {
		const card = this.getStoreCardByName(storeName);
		await card.getByRole('button', { name: 'Edit' }).click();
	}

	async clickDeleteStore(storeName: string) {
		const card = this.getStoreCardByName(storeName);
		await card.getByRole('button', { name: 'Delete' }).click();
	}

	async expectStoreVisible(storeName: string) {
		const card = this.getStoreCardByName(storeName);
		await expect(card).toBeVisible();
	}

	async expectStoreNotVisible(storeName: string) {
		const card = this.getStoreCardByName(storeName);
		await expect(card).not.toBeVisible();
	}

	async getStoreCount(): Promise<number> {
		await this.waitForStoresLoaded();
		return await this.storeCards.count();
	}
}
