import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class ComparisonListPage extends BasePage {
	readonly pageTitle: Locator;
	readonly createButton: Locator;
	readonly loadingSpinner: Locator;
	readonly emptyState: Locator;
	readonly totalGroupsCount: Locator;
	readonly totalProductsCount: Locator;

	constructor(page: Page) {
		super(page);
		this.pageTitle = page.getByRole('heading', { name: 'Comparison Groups', exact: true });
		this.createButton = page.getByRole('button', { name: 'Create Comparison' });
		this.loadingSpinner = page.locator('[data-testid="page-loading"]');
		this.emptyState = page.getByText('No comparison groups yet');
		// The stats are a slim inline summary (label + value spans), not cards.
		this.totalGroupsCount = page.getByTestId('stat-total-groups').locator('span.font-semibold');
		this.totalProductsCount = page
			.getByTestId('stat-total-products')
			.locator('span.font-semibold');
	}

	async goto() {
		await this.page.goto('/comparisons');
		await this.waitForPageLoad();
	}

	async waitForLoaded() {
		await expect(this.loadingSpinner).not.toBeVisible({ timeout: 10000 });
	}

	async clickCreate() {
		await this.createButton.click();
	}

	/** Find a group card by its name using the h3 heading inside each card */
	getGroupCardByName(name: string): Locator {
		// Each ComparisonCard has an h3 with the group name.
		// Navigate up to the card div (parent of the flex > div > h3 structure)
		return this.page.locator('h3').filter({ hasText: name }).locator('..').locator('..').locator('..');
	}

	async clickViewGroup(groupName: string) {
		const card = this.getGroupCardByName(groupName);
		await card.locator('button[title="View"]').click();
	}

	async clickDeleteGroup(groupName: string) {
		const card = this.getGroupCardByName(groupName);
		await card.locator('button[title="Delete"]').click();
	}

	async expectGroupVisible(name: string) {
		await expect(this.page.locator('h3').filter({ hasText: name })).toBeVisible();
	}

	async expectGroupNotVisible(name: string) {
		await expect(this.page.locator('h3').filter({ hasText: name })).not.toBeVisible();
	}

	async getGroupCount(): Promise<number> {
		await this.waitForLoaded();
		// Count h3 elements that are group names (inside the card grid)
		return await this.page.locator('button[title="View"]').count();
	}

	async expectGroupProductCount(groupName: string, count: number) {
		const card = this.getGroupCardByName(groupName);
		const expected = count === 1 ? '1 product' : `${count} products`;
		await expect(card.getByText(expected)).toBeVisible();
	}
}
