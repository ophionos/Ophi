import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { ComparisonListPage } from '../pages/comparison-list.page';
import { ComparisonDetailPage } from '../pages/comparison-detail.page';
import { ComparisonModalPage } from '../pages/comparison-modal.page';
import { AddProductModalPage } from '../pages/add-product-modal.page';
import { ConfirmModalPage } from '../pages/confirm-modal.page';

test.describe('Comparison Groups', () => {
	let listPage: ComparisonListPage;
	let detailPage: ComparisonDetailPage;
	let comparisonModal: ComparisonModalPage;
	let addProductModal: AddProductModalPage;
	let confirmModal: ConfirmModalPage;
	let api: TestApi;

	test.beforeEach(async ({ page }) => {
		listPage = new ComparisonListPage(page);
		detailPage = new ComparisonDetailPage(page);
		comparisonModal = new ComparisonModalPage(page);
		addProductModal = new AddProductModalPage(page);
		confirmModal = new ConfirmModalPage(page, 'Delete Comparison Group');
		api = new TestApi(page.request);
	});

	test.describe('View Comparisons List', () => {
		test('should display comparisons page with stats', async () => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await expect(listPage.pageTitle).toBeVisible();
			await expect(listPage.createButton).toBeVisible();
			await expect(listPage.totalGroupsCount).toBeVisible();
			await expect(listPage.totalProductsCount).toBeVisible();
		});

		test('should show empty state when no groups exist', async () => {
			// Clean up any existing test groups first
			const data = await api.getComparisons();
			for (const group of data.items ?? []) {
				if (group.name.startsWith('E2E Test')) {
					await api.deleteComparison(group.id);
				}
			}

			await listPage.goto();
			await listPage.waitForLoaded();

			// If user has no groups, empty state should show (or groups from other tests)
			const count = await listPage.getGroupCount();
			if (count === 0) {
				await expect(listPage.emptyState).toBeVisible();
			}
		});
	});

	test.describe('Create Comparison Group', () => {
		test('should open create modal when clicking Create Comparison', async () => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickCreate();
			await comparisonModal.expectOpen();
			await comparisonModal.expectCreateMode();
		});

		test('should create a new comparison group', async ({ page }) => {
			const groupName = `E2E Test Group ${Date.now()}`;

			await listPage.goto();
			await listPage.waitForLoaded();

			const initialCount = await listPage.getGroupCount();

			await listPage.clickCreate();
			await comparisonModal.expectOpen();

			const responsePromise = page.waitForResponse('**/api/v1/comparisons');
			await comparisonModal.fillAndSubmit(groupName, 'Test description');
			await responsePromise;

			await comparisonModal.expectClosed();
			await listPage.expectGroupVisible(groupName);

			const newCount = await listPage.getGroupCount();
			expect(newCount).toBe(initialCount + 1);

			// Cleanup
			const data = await api.getComparisons();
			const created = data.items.find((g) => g.name === groupName);
			if (created) {
				await api.deleteComparison(created.id);
			}
		});

		test('should close modal when clicking Cancel', async () => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickCreate();
			await comparisonModal.expectOpen();
			await comparisonModal.cancel();
			await comparisonModal.expectClosed();
		});

		test('should close modal when pressing Escape', async ({ page }) => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickCreate();
			await comparisonModal.expectOpen();
			await page.keyboard.press('Escape');
			await comparisonModal.expectClosed();
		});

		test('should show product count on group card', async ({ page }) => {
			const groupName = `E2E Test Group ${Date.now()}`;

			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickCreate();
			const responsePromise = page.waitForResponse('**/api/v1/comparisons');
			await comparisonModal.fillAndSubmit(groupName);
			await responsePromise;

			await comparisonModal.expectClosed();
			await listPage.expectGroupProductCount(groupName, 0);

			// Cleanup
			const data = await api.getComparisons();
			const created = data.items.find((g) => g.name === groupName);
			if (created) {
				await api.deleteComparison(created.id);
			}
		});
	});

	test.describe('Delete Comparison Group', () => {
		let testGroupId: string;
		let testGroupName: string;

		test.beforeEach(async () => {
			testGroupName = `E2E Test Group ${Date.now()}`;
			const group = await api.createComparison(testGroupName, 'To be deleted');
			testGroupId = group.id;
		});

		test.afterEach(async () => {
			if (testGroupId) {
				try {
					await api.deleteComparison(testGroupId);
				} catch {
					// Already deleted
				}
			}
		});

		test('should open delete confirmation modal', async () => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickDeleteGroup(testGroupName);
			await confirmModal.expectOpen();
			await expect(confirmModal.title).toHaveText('Delete Comparison Group');
		});

		test('should delete group when confirmed', async ({ page }) => {
			await listPage.goto();
			await listPage.waitForLoaded();

			const initialCount = await listPage.getGroupCount();

			await listPage.clickDeleteGroup(testGroupName);
			await confirmModal.expectOpen();

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/comparisons/') && resp.request().method() === 'DELETE'
			);
			await confirmModal.confirm();
			await responsePromise;

			await confirmModal.expectClosed();
			await listPage.expectGroupNotVisible(testGroupName);

			const newCount = await listPage.getGroupCount();
			expect(newCount).toBe(initialCount - 1);

			testGroupId = ''; // Already deleted
		});

		test('should cancel deletion when clicking Cancel', async () => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickDeleteGroup(testGroupName);
			await confirmModal.expectOpen();
			await confirmModal.cancel();
			await confirmModal.expectClosed();

			await listPage.expectGroupVisible(testGroupName);
		});
	});

	test.describe('Comparison Detail Page', () => {
		let testGroupId: string;
		let testGroupName: string;

		test.beforeEach(async () => {
			testGroupName = `E2E Test Group ${Date.now()}`;
			const group = await api.createComparison(testGroupName, 'Detail test group');
			testGroupId = group.id;
		});

		test.afterEach(async () => {
			if (testGroupId) {
				try {
					await api.deleteComparison(testGroupId);
				} catch {
					// Already deleted
				}
			}
		});

		test('should navigate to detail page from list', async ({ page }) => {
			await listPage.goto();
			await listPage.waitForLoaded();

			await listPage.clickViewGroup(testGroupName);
			await expect(page).toHaveURL(new RegExp(`/comparisons/${testGroupId}`));
			await detailPage.waitForLoaded();
			await detailPage.expectGroupName(testGroupName);
		});

		test('should display group details', async () => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.expectGroupName(testGroupName);
			await expect(detailPage.addProductButton).toBeVisible();
			await expect(detailPage.deleteGroupButton).toBeVisible();
			await expect(detailPage.chartSection).toBeVisible();
		});

		test('should show empty products state', async () => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.expectEmptyProducts();
		});

		test('should show chart day selector buttons', async () => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await expect(detailPage.dayButtons.filter({ hasText: '7d' })).toBeVisible();
			await expect(detailPage.dayButtons.filter({ hasText: '30d' })).toBeVisible();
			await expect(detailPage.dayButtons.filter({ hasText: '90d' })).toBeVisible();
		});

		test('should change chart time period', async ({ page }) => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			// Default is 7d — click 30d
			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/comparisons/') && resp.url().includes('days=30')
			);
			await detailPage.selectChartDays(30);
			await responsePromise;

			const button30d = detailPage.chartSection.getByRole('button', { name: '30d' });
			await expect(button30d).toHaveClass(/bg-brand/);
		});

		test('should navigate back to comparisons list', async ({ page }) => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.clickBack();
			await expect(page).toHaveURL(/\/comparisons$/);
		});

		test('should delete group from detail page', async ({ page }) => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.clickDeleteGroup();

			const deleteConfirm = new ConfirmModalPage(page, 'Delete Comparison Group');
			await deleteConfirm.expectOpen();

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/comparisons/') && resp.request().method() === 'DELETE'
			);
			await deleteConfirm.confirm();
			await responsePromise;

			await expect(page).toHaveURL(/\/comparisons$/);
			testGroupId = ''; // Already deleted
		});
	});

	test.describe('Add and Remove Products', () => {
		let testGroupId: string;
		let testGroupName: string;
		let testProductId1: string;
		let testProductId2: string;
		let productAName: string;

		test.beforeEach(async () => {
			testGroupName = `E2E Test Group ${Date.now()}`;
			const group = await api.createComparison(testGroupName);
			testGroupId = group.id;

			productAName = `E2E Product A ${Date.now()}`;
			const prod1 = await api.createProduct(productAName);
			testProductId1 = prod1.id;

			const prod2 = await api.createProduct(`E2E Product B ${Date.now()}`);
			testProductId2 = prod2.id;
		});

		test.afterEach(async () => {
			for (const id of [testProductId1, testProductId2]) {
				if (id) {
					try {
						await api.deleteProduct(id);
					} catch {
						// May already be deleted
					}
				}
			}
			if (testGroupId) {
				try {
					await api.deleteComparison(testGroupId);
				} catch {
					// May already be deleted
				}
			}
		});

		test('should open add product modal', async () => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.clickAddProduct();
			await addProductModal.expectOpen();
		});

		test('should add a product to the group', async ({ page }) => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.clickAddProduct();
			await addProductModal.expectOpen();

			// Wait for product list to load, then select and add
			// The exact name: a bare prefix matches leftovers from other runs / parallel workers.
			const productName = productAName;
			await addProductModal.selectProduct(productName);

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/products') && resp.request().method() === 'POST'
			);
			await addProductModal.addSelectedProduct();
			await responsePromise;

			await addProductModal.expectClosed();

			// Product should now appear in the group
			await detailPage.expectProductVisible(productName);
		});

		test('should remove a product from the group', async ({ page }) => {
			// Add product to group via API first
			await api.addProductToGroup(testGroupId, testProductId1);

			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			const productName = 'E2E Product A';
			await detailPage.expectProductVisible(productName);

			await detailPage.clickRemoveProduct(productName);

			// Confirm removal — the ConfirmModal uses confirmText="Remove"
			const removeModal = page.locator('[role="dialog"]').filter({ hasText: 'Remove Product' });
			await expect(removeModal).toBeVisible({ timeout: 5000 });

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/products/') && resp.request().method() === 'DELETE'
			);
			await removeModal.getByRole('button', { name: 'Remove' }).click();
			await responsePromise;

			await expect(removeModal).not.toBeVisible({ timeout: 5000 });
			await detailPage.expectEmptyProducts();
		});

		test('should close add product modal when clicking Cancel', async () => {
			await detailPage.goto(testGroupId);
			await detailPage.waitForLoaded();

			await detailPage.clickAddProduct();
			await addProductModal.expectOpen();
			await addProductModal.cancel();
			await addProductModal.expectClosed();
		});
	});
});
