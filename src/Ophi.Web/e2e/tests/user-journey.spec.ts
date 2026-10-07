import { test, expect } from '@playwright/test';
import { RegisterPage } from '../pages/register.page';
import { DashboardPage } from '../pages/dashboard.page';
import { ProductDetailPage } from '../pages/product-detail.page';
import { AlertModalPage } from '../pages/alert-modal.page';

test.describe('User Journey', () => {
	test('should register, add product, create alert, and verify alert is active', async ({
		browser
	}) => {
		test.setTimeout(60000);
		const context = await browser.newContext();
		const page = await context.newPage();

		const registerPage = new RegisterPage(page);
		const dashboardPage = new DashboardPage(page);
		const productDetailPage = new ProductDetailPage(page);
		const alertModal = new AlertModalPage(page);

		const timestamp = Date.now();
		const testName = 'Journey User';
		const testEmail = `journey-${timestamp}@example.com`;
		const testPassword = 'JourneyPassword123!';
		const productName = `Journey Product ${timestamp}`;

		let productId: string | undefined;

		try {
			await test.step('Register a new account', async () => {
				await registerPage.goto();
				await registerPage.register(testName, testEmail, testPassword);
				await registerPage.expectRegistrationSuccess();

				await dashboardPage.waitForDashboardLoaded();
				await dashboardPage.expectLoggedInAs(testName);
			});

			await test.step('Add a product', async () => {
				await dashboardPage.expectNoProducts();

				// Create a manual product (by name) — immediately Active, no scraping needed
				const response = await page.request.post('/api/v1/products/create', {
					data: { name: productName },
					headers: {
						'Content-Type': 'application/json',
						'X-Requested-With': 'XMLHttpRequest'
					}
				});
				expect(response.ok()).toBe(true);

				const body = await response.json();
				productId = body.id;
				expect(productId).toBeTruthy();

				// Reload the dashboard to see the new product. Use the auto-retrying
				// toHaveCount — a bare .count() races the client-side grid render.
				await page.reload();
				await dashboardPage.waitForDashboardLoaded();

				await expect(dashboardPage.productCards).toHaveCount(1, { timeout: 10000 });
			});

			await test.step('Navigate to product detail', async () => {
				await page.goto(`/products/${productId}`);

				await productDetailPage.waitForProductLoaded();
				await expect(page).toHaveURL(new RegExp(`/products/${productId}`));

				await expect(productDetailPage.productName).toBeVisible();
				await expect(productDetailPage.alertsSection).toBeVisible();
				await expect(productDetailPage.createAlertButton).toBeVisible();
				await productDetailPage.expectNoAlerts();
			});

			await test.step('Create a price alert', async () => {
				await productDetailPage.clickCreateAlert();
				await alertModal.expectOpen();

				// The page appends the created alert to local state — no product refetch
				// happens (by design), so only wait for the alert POST.
				const alertCreatePromise = page.waitForResponse(
					(resp) =>
						resp.url().includes('/api/v1/alerts') && resp.request().method() === 'POST'
				);

				await alertModal.fillAndSubmit(25, 'below');

				const alertResponse = await alertCreatePromise;
				expect(alertResponse.ok()).toBe(true);

				await alertModal.expectClosed();
			});

			await test.step('Verify alert is active', async () => {
				await expect(productDetailPage.alertItems.first()).toBeVisible({ timeout: 5000 });

				const alertCount = await productDetailPage.getAlertCount();
				expect(alertCount).toBe(1);

				await productDetailPage.expectAlertVisible('25.00', 'Below');

				const alertItem = productDetailPage.alertItems.first();
				await expect(alertItem.getByText('Active')).toBeVisible();
			});
		} finally {
			// Cleanup: delete the product (cascades to alerts)
			if (productId) {
				try {
					await page.request.delete(`/api/v1/products/${productId}`, {
						headers: { 'X-Requested-With': 'XMLHttpRequest' }
					});
				} catch {
					// Product may not exist if test failed early
				}
			}
			await context.close();
		}
	});
});
