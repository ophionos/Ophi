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
		this.totalProductsCount = page.getByTestId('stat-total-products').locator('span.font-semibold');
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

	/** Find a group card by the name in its h3 heading */
	getGroupCardByName(name: string): Locator {
		return this.page
			.getByTestId('comparison-card')
			.filter({ has: this.page.locator('h3', { hasText: name }) });
	}

	/** The card has no View button: the name link stretches over the card and opens the group. */
	async clickViewGroup(groupName: string) {
		const card = this.getGroupCardByName(groupName);
		await card.getByRole('link', { name: groupName }).click();
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
		return await this.page.getByTestId('comparison-card').count();
	}

	async expectGroupProductCount(groupName: string, count: number) {
		const card = this.getGroupCardByName(groupName);
		const expected = count === 1 ? '1 product' : `${count} products`;
		await expect(card.getByText(expected)).toBeVisible();
	}
}
