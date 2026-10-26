// Baskets e2e. The suite cannot seed prices, so the products here have no price and no store.
// The spec therefore asserts the items and the "no store" state, never per-store totals; the
// totals are covered by the component tests (BasketStoreTable, BasketSplitCard).
import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { DashboardPage } from '../pages/dashboard.page';

test.describe('Baskets', () => {
	let dashboard: DashboardPage;
	let api: TestApi;
	const productIds: string[] = [];
	const basketIds: string[] = [];

	test.beforeEach(async ({ page }) => {
		dashboard = new DashboardPage(page);
		api = new TestApi(page.request);
		// The onboarding tour's tooltip sits over the Add to basket dialog's buttons on a fresh account.
		await page.addInitScript(() =>
			localStorage.setItem(
				'ophi-onboarding',
				JSON.stringify({ dismissed: true, completed: false, currentStep: 0 })
			)
		);
	});

	test.afterEach(async () => {
		for (const id of basketIds) await api.delete(`/baskets/${id}`);
		for (const id of productIds) await api.deleteProduct(id).catch(() => undefined);
		basketIds.length = 0;
		productIds.length = 0;
	});

	test('should list both products in a basket when added from the watchlist selection', async ({
		page
	}) => {
		const stamp = Date.now();
		const basketName = `E2E Basket ${stamp}`;
		const nameA = `E2E Basket Item A ${stamp}`;
		const nameB = `E2E Basket Item B ${stamp}`;
		for (const name of [nameA, nameB]) {
			productIds.push((await api.createProduct(name)).id);
		}

		// Create the basket from /baskets.
		await page.goto('/baskets');
		await page.locator('body[data-hydrated]').waitFor({ state: 'attached' });
		await expect(async () => {
			await page.getByRole('button', { name: 'New basket' }).first().click();
			await expect(page.getByRole('dialog', { name: 'New basket' })).toBeVisible({ timeout: 1000 });
		}).toPass();
		const dialog = page.getByRole('dialog', { name: 'New basket' });
		await dialog.getByLabel(/Name/).fill(basketName);
		const created = page.waitForResponse(
			(r) => r.url().endsWith('/api/v1/baskets') && r.request().method() === 'POST'
		);
		await dialog.getByRole('button', { name: 'Create' }).click();
		const basket = await (await created).json();
		basketIds.push(basket.id);
		await expect(page.getByRole('link', { name: basketName })).toBeVisible();

		// Select both products in the watchlist and bulk-add them.
		await dashboard.goto();
		await dashboard.waitForDashboardLoaded();
		await dashboard.search(`E2E Basket Item`);
		await expect(async () => {
			await page.getByTestId('bulk-select-toggle').click();
			await expect(page.getByTestId('bulk-bar')).toBeVisible({ timeout: 1000 });
		}).toPass();
		await page.getByTestId('bulk-select-page').click();
		await expect(page.getByText('2 selected')).toBeVisible();
		await page.getByTestId('bulk-add-to-basket').click();

		const addDialog = page.getByRole('dialog', { name: 'Add to basket' });
		await addDialog.getByLabel(basketName).check();
		const added = page.waitForResponse(
			(r) => r.url().includes(`/api/v1/baskets/${basket.id}/items`) && r.request().method() === 'POST'
		);
		await addDialog.getByRole('button', { name: 'Add to basket' }).click();
		expect((await added).ok()).toBe(true);

		// Open the basket: both items are listed, and with no prices there is no store yet.
		await page.goto(`/baskets/${basket.id}`);
		await page.locator('body[data-hydrated]').waitFor({ state: 'attached' });
		const items = page.getByTestId('basket-items');
		await expect(items.getByRole('link', { name: nameA })).toBeVisible();
		await expect(items.getByRole('link', { name: nameB })).toBeVisible();
		await expect(page.getByTestId('basket-store-row')).toHaveCount(0);
	});
});
