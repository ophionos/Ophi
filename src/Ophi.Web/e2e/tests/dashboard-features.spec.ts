import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { DashboardPage } from '../pages/dashboard.page';

test.describe('Dashboard Features', () => {
	let dashboard: DashboardPage;
	let api: TestApi;
	const createdProductIds: string[] = [];

	test.beforeEach(async ({ page }) => {
		dashboard = new DashboardPage(page);
		api = new TestApi(page.request);
	});

	// Helper to create test products via API
	async function createTestProduct(name: string): Promise<string> {
		const product = await api.createProduct(name);
		createdProductIds.push(product.id);
		return product.id;
	}

	test.afterEach(async () => {
		// Clean up created products
		for (const id of createdProductIds) {
			try {
				await api.deleteProduct(id);
			} catch {
				// May already be deleted
			}
		}
		createdProductIds.length = 0;
	});

	test.describe('Stats Bar', () => {
		test('should display stats bar with counts', async () => {
			await createTestProduct(`E2E Stats Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await expect(dashboard.statsBar).toBeVisible();
		});
	});

	test.describe('Search', () => {
		test('should filter products by search term', async () => {
			const uniqueName = `UniqueSearchTarget-${Date.now()}`;
			await createTestProduct(uniqueName);
			await createTestProduct(`E2E Other Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			// Search for the unique name
			await dashboard.search('UniqueSearchTarget');

			await dashboard.expectProductVisible(uniqueName);
		});

		test('should show message when search has no results', async () => {
			await createTestProduct(`E2E Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.search('nonexistentrandomquery99999');
			await dashboard.expectEmptyFilterResult('No products matching');
		});
	});

	test.describe('Sort', () => {
		test('should display sort dropdown', async () => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await expect(dashboard.sortSelect).toBeVisible();
		});

		test('should change sort order', async ({ page }) => {
			await createTestProduct(`E2E Sort Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			// Switch to "Name" ascending
			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/products') && resp.url().includes('sortBy')
			);
			await dashboard.selectSort('name:asc');
			await responsePromise;
		});
	});

	test.describe('Status Filters', () => {
		test('should display status filter chips', async () => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await expect(dashboard.statusFilters).toBeVisible();
			await expect(dashboard.page.locator('[data-testid="status-chip-all"]')).toBeVisible();
			await expect(dashboard.page.locator('[data-testid="status-chip-active"]')).toBeVisible();
			await expect(dashboard.page.locator('[data-testid="status-chip-paused"]')).toBeVisible();
			await expect(dashboard.page.locator('[data-testid="status-chip-error"]')).toBeVisible();
		});

		test('should filter by active status', async ({ page }) => {
			await createTestProduct(`E2E Active Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/products') && resp.url().includes('status=active')
			);
			await dashboard.clickStatusFilter('active');
			await responsePromise;
		});

		test('should show empty message for error status when no errors exist', async ({ page }) => {
			await createTestProduct(`E2E Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/products') && resp.url().includes('status=error')
			);
			await dashboard.clickStatusFilter('error');
			await responsePromise;

			await dashboard.expectEmptyFilterResult('No products with errors');
		});
	});

	test.describe('View Modes', () => {
		test('should display view mode toggle buttons', async () => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await expect(dashboard.viewToggleGrid).toBeVisible();
			await expect(dashboard.viewToggleList).toBeVisible();
			await expect(dashboard.viewToggleFeed).toBeVisible();
		});

		test('should switch to list view', async () => {
			await createTestProduct(`E2E View Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.switchToListView();

			// Product table should appear in list view
			await expect(dashboard.productTable).toBeVisible();
		});

		test('should switch back to grid view', async () => {
			await createTestProduct(`E2E View Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			// Switch to list first
			await dashboard.switchToListView();
			await expect(dashboard.productTable).toBeVisible();

			// Switch back to grid
			await dashboard.switchToGridView();
			await expect(dashboard.productTable).not.toBeVisible();
		});

		test('should switch to feed view', async () => {
			await createTestProduct(`E2E View Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.switchToFeedView();

			// Grid and table should not be visible
			await expect(dashboard.productTable).not.toBeVisible();
			// Feed view is now active (aria-pressed)
			await expect(dashboard.viewToggleFeed).toHaveAttribute('aria-pressed', 'true');
		});

		test('should have correct aria-pressed on active view toggle', async () => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			// Default is grid
			await expect(dashboard.viewToggleGrid).toHaveAttribute('aria-pressed', 'true');
			await expect(dashboard.viewToggleList).toHaveAttribute('aria-pressed', 'false');
			await expect(dashboard.viewToggleFeed).toHaveAttribute('aria-pressed', 'false');

			await dashboard.switchToListView();
			await expect(dashboard.viewToggleGrid).toHaveAttribute('aria-pressed', 'false');
			await expect(dashboard.viewToggleList).toHaveAttribute('aria-pressed', 'true');
		});
	});

	test.describe('Create from Scratch', () => {
		test('should open create product modal', async ({ page }) => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.clickCreateFromScratch();

			// Modal should appear with product name field
			const nameInput = page.locator('[data-testid="create-product-name"]');
			await expect(nameInput).toBeVisible({ timeout: 5000 });
		});

		test('should create a product from scratch', async ({ page }) => {
			const productName = `E2E Scratch Product ${Date.now()}`;

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.clickCreateFromScratch();

			const nameInput = page.locator('[data-testid="create-product-name"]');
			await nameInput.fill(productName);

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/products') && resp.request().method() === 'POST'
			);
			const submitButton = page.locator('[data-testid="create-product-submit"]');
			await submitButton.click();
			const response = await responsePromise;
			const created = await response.json();
			createdProductIds.push(created.id);

			// Should show on dashboard
			await dashboard.expectProductVisible(productName);
		});

		test('should show validation error for empty name', async ({ page }) => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.clickCreateFromScratch();

			const submitButton = page.locator('[data-testid="create-product-submit"]');
			await submitButton.click();

			const nameError = page.locator('[data-testid="name-error"]');
			await expect(nameError).toBeVisible();
			await expect(nameError).toContainText('Name is required');
		});

		test('should close modal when clicking Cancel or pressing Escape', async ({ page }) => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await dashboard.clickCreateFromScratch();

			const nameInput = page.locator('[data-testid="create-product-name"]');
			await expect(nameInput).toBeVisible();

			await page.keyboard.press('Escape');
			await expect(nameInput).not.toBeVisible();
		});
	});

	test.describe('Favourite Filter', () => {
		test('should display favourite filter button', async () => {
			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			await expect(dashboard.favouriteFilter).toBeVisible();
		});

		test('should toggle favourite filter', async () => {
			await createTestProduct(`E2E Product ${Date.now()}`);

			await dashboard.goto();
			await dashboard.waitForDashboardLoaded();

			// Click favourite filter — should show empty since no products are favourited
			await dashboard.clickFavouriteFilter();

			await dashboard.expectEmptyFilterResult('No favourite products');
		});
	});
});
