import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { ProductDetailPage } from '../pages/product-detail.page';
import { AlertModalPage } from '../pages/alert-modal.page';
import { ConfirmModalPage } from '../pages/confirm-modal.page';

test.describe('Product Detail Page', () => {
	let productDetailPage: ProductDetailPage;
	let alertModal: AlertModalPage;
	let confirmModal: ConfirmModalPage;
	let api: TestApi;
	let testProductId: string;

	test.beforeEach(async ({ page }) => {
		productDetailPage = new ProductDetailPage(page);
		alertModal = new AlertModalPage(page);
		confirmModal = new ConfirmModalPage(page, 'Delete Product');
		api = new TestApi(page.request);

		// Create a manual test product (no URL, no scraping needed — immediately Active)
		const product = await api.createProduct(`Test Product ${Date.now()}`);
		testProductId = product.id;
	});

	test.afterEach(async () => {
		// Clean up - delete the test product if it still exists
		if (testProductId) {
			try {
				await api.deleteProduct(testProductId);
			} catch {
				// Product may have been deleted during test
			}
		}
	});

	test.describe('View Product Details', () => {
		test('should display product information', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await expect(productDetailPage.productName).toBeVisible();
			await expect(productDetailPage.statusBadge).toBeVisible();
		});

		test('should display statistics or single price section', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			// For newly created products with only one price point, the page shows
			// a single price display instead of the 4-card statistics grid
			const singlePriceDisplay = page.locator('[data-testid="single-price-display"]');
			const statsGrid = page.locator('.grid.grid-cols-2');

			// One of the two should be visible
			const hasSinglePrice = await singlePriceDisplay.isVisible();
			const hasStatsGrid = await statsGrid.isVisible();

			expect(hasSinglePrice || hasStatsGrid).toBeTruthy();
		});

		test('should display price history chart section', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await expect(productDetailPage.priceChartSection).toBeVisible();
			// Day selector buttons should be visible
			await expect(productDetailPage.chartDayButtons.filter({ hasText: '7d' })).toBeVisible();
			await expect(productDetailPage.chartDayButtons.filter({ hasText: '30d' })).toBeVisible();
			await expect(productDetailPage.chartDayButtons.filter({ hasText: '90d' })).toBeVisible();
		});

		test('should display alerts section', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await expect(productDetailPage.alertsSection).toBeVisible();
			await expect(productDetailPage.createAlertButton).toBeVisible();
		});

		test('should display danger zone section', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			// Danger zone is collapsible — verify it exists and can be opened
			await expect(productDetailPage.dangerZone).toBeVisible();
			await productDetailPage.openDangerZone();
			await expect(productDetailPage.deleteProductButton).toBeVisible();
		});

		test('should navigate back to dashboard when clicking back button', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickBack();
			await expect(page).toHaveURL('/dashboard');
		});

		test('should change chart time period when clicking day buttons', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			// Click 7 days button and verify it's selected (has different styling)
			await productDetailPage.selectChartDays(7);
			const button7d = productDetailPage.priceChartSection.getByRole('button', { name: '7d' });
			await expect(button7d).toHaveClass(/bg-brand/);

			// Click 90 days button and verify styling changes
			await productDetailPage.selectChartDays(90);
			const button90d = productDetailPage.priceChartSection.getByRole('button', { name: '90d' });
			await expect(button90d).toHaveClass(/bg-brand/);
		});
	});

	test.describe('Alert Management', () => {
		test('should open create alert modal when clicking Create Alert button', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickCreateAlert();
			await alertModal.expectOpen();
		});

		test('should close alert modal when clicking Cancel', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickCreateAlert();
			await alertModal.expectOpen();
			await alertModal.cancel();
			await alertModal.expectClosed();
		});

		test('should close alert modal when pressing Escape', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickCreateAlert();
			await alertModal.expectOpen();
			await page.keyboard.press('Escape');
			await alertModal.expectClosed();
		});

		test('should create a new alert', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			// Verify no alerts initially
			const initialCount = await productDetailPage.getAlertCount();

			await productDetailPage.clickCreateAlert();
			await alertModal.expectOpen();

			// Wait for the alert creation only — the page appends the created alert to
			// local state instead of refetching the product (by design).
			const alertCreatePromise = page.waitForResponse('**/api/v1/alerts');
			await alertModal.fillAndSubmit(50, 'below');
			await alertCreatePromise;

			await alertModal.expectClosed();

			// Wait for the alert item to appear in the DOM
			await expect(productDetailPage.alertItems.first()).toBeVisible({ timeout: 5000 });

			// Verify alert was created
			const newCount = await productDetailPage.getAlertCount();
			expect(newCount).toBe(initialCount + 1);
		});

		test('should delete an alert', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			// First create an alert
			await productDetailPage.clickCreateAlert();
			const createResponse = page.waitForResponse('**/api/v1/alerts');
			await alertModal.fillAndSubmit(50, 'below');
			await createResponse;
			await alertModal.expectClosed();

			// Wait for the product to reload and show the new alert
			await expect(productDetailPage.alertItems.first()).toBeVisible({ timeout: 10000 });

			const initialCount = await productDetailPage.getAlertCount();
			expect(initialCount).toBeGreaterThan(0);

			// Now delete it — the page filters the alert out of local state, no
			// product refetch happens (by design).
			const deleteResponse = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/alerts/') && resp.request().method() === 'DELETE'
			);
			await productDetailPage.deleteAlertByIndex(0);
			await deleteResponse;

			// Wait for the UI to reflect the deletion
			await expect(productDetailPage.alertItems).toHaveCount(initialCount - 1, { timeout: 10000 });

			// Verify alert was deleted
			const newCount = await productDetailPage.getAlertCount();
			expect(newCount).toBe(initialCount - 1);
		});
	});

	test.describe('Delete Product', () => {
		test('should open delete confirmation modal', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickDeleteProduct();
			await confirmModal.expectOpen();
			await expect(confirmModal.title).toHaveText('Delete Product');
		});

		test('should cancel deletion when clicking Cancel', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickDeleteProduct();
			await confirmModal.expectOpen();
			await confirmModal.cancel();
			await confirmModal.expectClosed();

			// Should still be on the product page
			await expect(productDetailPage.productName).toBeVisible();
		});

		test('should delete product and redirect to dashboard', async ({ page }) => {
			await productDetailPage.goto(testProductId);
			await productDetailPage.waitForProductLoaded();

			await productDetailPage.clickDeleteProduct();
			await confirmModal.expectOpen();

			const responsePromise = page.waitForResponse(
				(resp) =>
					resp.url().includes(`/api/v1/products/${testProductId}`) &&
					resp.request().method() === 'DELETE'
			);
			await confirmModal.confirm();
			await responsePromise;

			// Should redirect to dashboard
			await expect(page).toHaveURL('/dashboard');

			// Mark as deleted so cleanup doesn't try to delete again
			testProductId = '';
		});
	});
});
