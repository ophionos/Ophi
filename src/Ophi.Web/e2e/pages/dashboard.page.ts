import { Page, Locator, Response, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class DashboardPage extends BasePage {
	readonly pageTitle: Locator;
	readonly dashboardLink: Locator;
	readonly loadingSpinner: Locator;

	// Add Product Form
	readonly productUrlInput: Locator;
	readonly addProductButton: Locator;

	// Product Grid
	readonly productCards: Locator;
	readonly emptyProductsState: Locator;

	// Stats bar
	readonly statsBar: Locator;

	// Filters
	readonly searchInput: Locator;
	readonly sortSelect: Locator;
	readonly statusFilters: Locator;
	readonly favouriteFilter: Locator;

	// View toggles
	readonly viewToggleGrid: Locator;
	readonly viewToggleList: Locator;
	readonly viewToggleFeed: Locator;

	// Pagination
	readonly paginationControls: Locator;
	readonly paginationPrev: Locator;
	readonly paginationNext: Locator;
	readonly paginationInfo: Locator;

	// Product table (list view)
	readonly productTable: Locator;

	// Create from scratch
	readonly createFromScratchLink: Locator;

	// Empty filter result
	readonly emptyFilterResult: Locator;

	constructor(page: Page) {
		super(page);
		this.pageTitle = page.getByRole('heading', { name: 'Your Products' });
		this.dashboardLink = page.getByRole('link', { name: 'Dashboard' });
		this.loadingSpinner = page.locator('[data-testid="page-loading"]');

		// Add Product Form
		this.productUrlInput = page.getByPlaceholder('Paste product URL...');
		this.addProductButton = page.getByRole('button', { name: 'Add' });

		// Product Grid
		this.productCards = page.locator('[data-testid="product-card"]');
		this.emptyProductsState = page.getByRole('heading', { name: 'Start tracking prices' });

		// Stats bar
		this.statsBar = page.locator('[data-testid="stats-bar"]');

		// Filters
		this.searchInput = page.locator('[data-testid="search-input"]');
		this.sortSelect = page.locator('[data-testid="sort-select"]');
		this.statusFilters = page.locator('[data-testid="status-filters"]');
		this.favouriteFilter = page.locator('[data-testid="favourite-filter"]');

		// View toggles
		this.viewToggleGrid = page.locator('[data-testid="view-toggle-grid"]');
		this.viewToggleList = page.locator('[data-testid="view-toggle-list"]');
		this.viewToggleFeed = page.locator('[data-testid="view-toggle-feed"]');

		// Pagination
		this.paginationControls = page.locator('[data-testid="pagination-controls"]');
		this.paginationPrev = page.locator('[data-testid="pagination-prev"]');
		this.paginationNext = page.locator('[data-testid="pagination-next"]');
		this.paginationInfo = page.locator('[data-testid="pagination-info"]');

		// Product table (list view)
		this.productTable = page.locator('[data-testid="product-table"]');

		// Create from scratch
		this.createFromScratchLink = page.locator('[data-testid="create-from-scratch"]');

		// Empty filter result
		this.emptyFilterResult = page.locator('[data-testid="empty-filter-result"]');
	}

	async goto() {
		await this.page.goto('/dashboard');
		await this.waitForPageLoad();
	}

	async waitForDashboardLoaded() {
		await expect(this.loadingSpinner).not.toBeVisible({ timeout: 10000 });
		await expect(this.pageTitle).toBeVisible({ timeout: 10000 });
	}

	async expectLoggedInAs(name: string) {
		// Wait for user menu to be available
		const userMenu = this.page.locator('[data-user-menu]');
		await expect(userMenu).toBeVisible({ timeout: 10000 });

		const userMenuButton = userMenu.locator('button').first();
		await userMenuButton.click();

		// Wait for dropdown to open and check name
		await expect(userMenu.getByText(name)).toBeVisible({ timeout: 5000 });

		// Close menu by pressing Escape (more reliable than clicking toggle)
		await this.page.keyboard.press('Escape');
		await expect(userMenu.getByText(name)).not.toBeVisible({ timeout: 3000 });
	}

	async logout() {
		// Wait for user menu to be available
		const userMenu = this.page.locator('[data-user-menu]');
		await expect(userMenu).toBeVisible({ timeout: 10000 });

		const userMenuButton = userMenu.locator('button').first();
		const responsePromise = this.page.waitForResponse(
			(resp) => resp.url().includes('/api/v1/auth/logout'),
			{ timeout: 10000 }
		);
		await userMenuButton.click();
		// Wait for dropdown to appear before clicking Logout
		const logoutButton = userMenu.getByText('Logout');
		await expect(logoutButton).toBeVisible({ timeout: 5000 });
		await logoutButton.click();
		await responsePromise;
	}

	async navigateToStores() {
		// Stores is now under the settings dropdown
		const settingsMenu = this.page.locator('[data-settings-menu]');
		const settingsButton = settingsMenu.locator('button').first();
		await settingsButton.click();
		const storesLink = settingsMenu.getByRole('link', { name: 'Stores' });
		await expect(storesLink).toBeVisible({ timeout: 5000 });
		await storesLink.click();
		await expect(this.page).toHaveURL(/\/stores/);
	}

	async navigateToDashboard() {
		await this.dashboardLink.click();
		await expect(this.page).toHaveURL(/\/dashboard/);
	}

	async expectOnDashboard() {
		await expect(this.page).toHaveURL(/\/dashboard/);
		await expect(this.pageTitle).toBeVisible();
	}

	async expectNavVisible() {
		await expect(this.dashboardLink).toBeVisible();
	}

	async addProduct(url: string): Promise<Response> {
		await this.productUrlInput.fill(url);
		const responsePromise = this.page.waitForResponse(
			(resp) => resp.url().includes('/api/v1/products') && resp.request().method() === 'POST',
			{ timeout: 10000 }
		);
		await this.addProductButton.click();
		return responsePromise;
	}

	async waitForProductCardReady(timeout: number = 30000) {
		const loadingText = this.page.getByText('Loading product data...');
		await expect(loadingText).not.toBeVisible({ timeout });
	}

	async getProductCount(): Promise<number> {
		return this.productCards.count();
	}

	async clickFirstProductHistory() {
		await this.page.locator('[title="View history"]').first().click();
		await expect(this.page).toHaveURL(/\/products\//);
	}

	async expectNoProducts() {
		await expect(this.emptyProductsState).toBeVisible();
	}

	async expectProductVisible(text: string) {
		// Use the product name link which is more reliable than CSS class matching
		const productLink = this.page.getByRole('link', { name: text });
		await expect(productLink.first()).toBeVisible();
	}

	// --- Filter & View methods ---

	async search(query: string) {
		// Wait for the filtered products fetch, not `networkidle` (the SSE
		// connection keeps the network busy forever — see BasePage.waitForPageLoad).
		const responsePromise = this.page.waitForResponse(
			(resp) => resp.url().includes('/api/v1/products') && resp.url().includes('search=')
		);
		await this.searchInput.fill(query);
		await responsePromise;
	}

	async clearSearch() {
		const responsePromise = this.page.waitForResponse((resp) =>
			resp.url().includes('/api/v1/products')
		);
		await this.searchInput.clear();
		await responsePromise;
	}

	async selectSort(value: string) {
		await this.sortSelect.selectOption(value);
	}

	async clickStatusFilter(status: 'all' | 'active' | 'paused' | 'error') {
		const chip = this.page.locator(`[data-testid="status-chip-${status}"]`);
		await chip.click();
	}

	async clickFavouriteFilter() {
		await this.favouriteFilter.click();
	}

	async switchToGridView() {
		await this.viewToggleGrid.click();
	}

	async switchToListView() {
		await this.viewToggleList.click();
	}

	async switchToFeedView() {
		await this.viewToggleFeed.click();
	}

	async clickCreateFromScratch() {
		await this.createFromScratchLink.click();
	}

	async expectEmptyFilterResult(message?: string) {
		await expect(this.emptyFilterResult).toBeVisible();
		if (message) {
			await expect(this.emptyFilterResult).toContainText(message);
		}
	}
}
