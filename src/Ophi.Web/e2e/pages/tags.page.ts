import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class TagsPage extends BasePage {
	readonly pageTitle: Locator;
	readonly createButton: Locator;
	readonly loadingSpinner: Locator;
	readonly tagList: Locator;
	readonly tagListItems: Locator;
	readonly emptyState: Locator;
	readonly totalTagsCount: Locator;
	readonly taggedProductsCount: Locator;

	constructor(page: Page) {
		super(page);
		this.pageTitle = page.getByRole('heading', { name: 'Tags', exact: true });
		this.createButton = page.locator('[data-testid="create-tag-button"]');
		this.loadingSpinner = page.locator('[data-testid="page-loading"]');
		this.tagList = page.locator('[data-testid="tag-list"]');
		this.tagListItems = page.locator('[data-testid="tag-list-item"]');
		this.emptyState = page.getByText('No tags yet');
		this.totalTagsCount = page.getByTestId('stat-total-tags').locator('span.font-semibold');
		this.taggedProductsCount = page.getByTestId('stat-tagged-products').locator('span.font-semibold');
	}

	async goto() {
		await this.page.goto('/tags');
		await this.waitForPageLoad();
	}

	async waitForLoaded() {
		await expect(this.loadingSpinner).not.toBeVisible({ timeout: 10000 });
	}

	async clickCreate() {
		await this.createButton.click();
	}

	getTagItemByName(name: string): Locator {
		return this.tagListItems.filter({ hasText: name });
	}

	async clickEditTag(tagName: string) {
		const item = this.getTagItemByName(tagName);
		await item.locator('[data-testid="tag-edit-button"]').click();
	}

	async clickDeleteTag(tagName: string) {
		const item = this.getTagItemByName(tagName);
		await item.locator('[data-testid="tag-delete-button"]').click();
	}

	async expectTagVisible(name: string) {
		await expect(this.getTagItemByName(name)).toBeVisible();
	}

	async expectTagNotVisible(name: string) {
		await expect(this.getTagItemByName(name)).not.toBeVisible();
	}

	async getTagCount(): Promise<number> {
		await this.waitForLoaded();
		return await this.tagListItems.count();
	}

	async expectTagProductCount(tagName: string, count: number) {
		const item = this.getTagItemByName(tagName);
		const expected = count === 1 ? '1 product' : `${count} products`;
		await expect(item.getByText(expected)).toBeVisible();
	}
}
