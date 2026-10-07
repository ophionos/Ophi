import { test, expect } from '../fixtures/auth.fixture';
import { StoresPage } from '../pages/stores.page';
import { StoreModalPage } from '../pages/store-modal.page';
import { ConfirmModalPage } from '../pages/confirm-modal.page';
import { createTestStoreData, BUILT_IN_STORES } from '../fixtures/test-data';

test.describe('Store Configuration Management', () => {
	let storesPage: StoresPage;
	let storeModal: StoreModalPage;
	let confirmModal: ConfirmModalPage;

	test.beforeEach(async ({ page }) => {
		storesPage = new StoresPage(page);
		storeModal = new StoreModalPage(page);
		confirmModal = new ConfirmModalPage(page, 'Delete Store');
	});

	test.describe('View Stores', () => {
		test('should display stores page with stats', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await expect(storesPage.pageTitle).toBeVisible();
			await expect(storesPage.addStoreButton).toBeVisible();
			await expect(storesPage.totalStoresCount).toBeVisible();
			await expect(storesPage.builtInStoresCount).toBeVisible();
			await expect(storesPage.customStoresCount).toBeVisible();
		});

		test('should display built-in stores', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			// Verify at least one built-in store is visible
			for (const storeName of BUILT_IN_STORES.slice(0, 3)) {
				await storesPage.expectStoreVisible(storeName);
			}
		});

		test('should show built-in badge for built-in stores', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			const amazonCard = storesPage.getStoreCardByName('Amazon');
			await expect(amazonCard.getByText('Built-in')).toBeVisible();
		});

		test('should not show edit/delete buttons for built-in stores', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			const amazonCard = storesPage.getStoreCardByName('Amazon');
			await expect(amazonCard.getByRole('button', { name: 'Edit' })).not.toBeVisible();
			await expect(amazonCard.getByRole('button', { name: 'Delete' })).not.toBeVisible();
		});
	});

	test.describe('Create Store', () => {
		test('should open create modal when clicking Add Store', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await storesPage.clickAddStore();
			await storeModal.expectOpen();
			await storeModal.expectCreateMode();
		});

		test('should create a new custom store', async ({ page }) => {
			const testStore = createTestStoreData();

			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			const initialCount = await storesPage.getStoreCount();

			await storesPage.clickAddStore();
			await storeModal.fillForm(testStore);

			// Wait for API response
			const responsePromise = page.waitForResponse('**/api/v1/stores');
			await storeModal.submit();
			await responsePromise;

			await storeModal.expectClosed();
			await storesPage.expectStoreVisible(testStore.name);

			// Verify store count increased
			const newCount = await storesPage.getStoreCount();
			expect(newCount).toBe(initialCount + 1);
		});

		test('should show custom badge for newly created store', async ({ page }) => {
			const testStore = createTestStoreData();

			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await storesPage.clickAddStore();
			await storeModal.fillForm(testStore);

			// Wait for API response before checking modal closed
			const responsePromise = page.waitForResponse('**/api/v1/stores');
			await storeModal.submit();
			await responsePromise;

			await storeModal.expectClosed();

			const customCard = storesPage.getStoreCardByName(testStore.name);
			await expect(customCard.getByText('Custom')).toBeVisible();
		});

		test('should close modal when clicking Cancel', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await storesPage.clickAddStore();
			await storeModal.expectOpen();
			await storeModal.cancel();
			await storeModal.expectClosed();
		});

		test('should close modal when pressing Escape', async ({ page }) => {
			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await storesPage.clickAddStore();
			await storeModal.expectOpen();
			await page.keyboard.press('Escape');
			await storeModal.expectClosed();
		});
	});

	test.describe('Edit Store', () => {
		let createdStoreName: string;

		test.beforeEach(async ({ page }) => {
			// Create a store to edit
			const testStore = createTestStoreData();
			createdStoreName = testStore.name;

			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await storesPage.clickAddStore();
			await storeModal.fillForm(testStore);

			// Wait for API response before checking modal closed
			const responsePromise = page.waitForResponse('**/api/v1/stores');
			await storeModal.submit();
			await responsePromise;

			await storeModal.expectClosed();
			await storesPage.expectStoreVisible(createdStoreName);
		});

		test('should open edit modal with existing data', async ({ page }) => {
			await storesPage.clickEditStore(createdStoreName);
			await storeModal.expectOpen();
			await storeModal.expectEditMode();

			// Verify name is pre-filled
			await expect(storeModal.nameInput).toHaveValue(createdStoreName);
		});

		test('should update store name', async ({ page }) => {
			// Use a completely different name (not containing the original) to avoid partial match issues
			const updatedName = `Renamed Store ${Date.now()}`;

			await storesPage.clickEditStore(createdStoreName);
			await storeModal.nameInput.clear();
			await storeModal.nameInput.fill(updatedName);

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/stores/') && resp.request().method() === 'PUT'
			);
			await storeModal.submit();
			await responsePromise;

			await storeModal.expectClosed();
			await storesPage.expectStoreVisible(updatedName);
			await storesPage.expectStoreNotVisible(createdStoreName);
		});

		test('should show edit/delete buttons for custom stores', async ({ page }) => {
			const customCard = storesPage.getStoreCardByName(createdStoreName);
			await expect(customCard.getByRole('button', { name: 'Edit' })).toBeVisible();
			await expect(customCard.getByRole('button', { name: 'Delete' })).toBeVisible();
		});
	});

	test.describe('Delete Store', () => {
		let createdStoreName: string;

		test.beforeEach(async ({ page }) => {
			// Create a store to delete
			const testStore = createTestStoreData();
			createdStoreName = testStore.name;

			await storesPage.goto();
			await storesPage.waitForStoresLoaded();

			await storesPage.clickAddStore();
			await storeModal.fillForm(testStore);

			// Wait for API response before checking modal closed
			const responsePromise = page.waitForResponse('**/api/v1/stores');
			await storeModal.submit();
			await responsePromise;

			await storeModal.expectClosed();
			await storesPage.expectStoreVisible(createdStoreName);
		});

		test('should open delete confirmation modal', async ({ page }) => {
			await storesPage.clickDeleteStore(createdStoreName);
			await confirmModal.expectOpen();
			await expect(confirmModal.title).toHaveText('Delete Store');
		});

		test('should delete store when confirmed', async ({ page }) => {
			const initialCount = await storesPage.getStoreCount();

			await storesPage.clickDeleteStore(createdStoreName);
			await confirmModal.expectOpen();

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/stores/') && resp.request().method() === 'DELETE'
			);
			await confirmModal.confirm();
			await responsePromise;

			await confirmModal.expectClosed();
			await storesPage.expectStoreNotVisible(createdStoreName);

			const newCount = await storesPage.getStoreCount();
			expect(newCount).toBe(initialCount - 1);
		});

		test('should cancel deletion when clicking Cancel', async ({ page }) => {
			await storesPage.clickDeleteStore(createdStoreName);
			await confirmModal.expectOpen();
			await confirmModal.cancel();
			await confirmModal.expectClosed();

			// Store should still exist
			await storesPage.expectStoreVisible(createdStoreName);
		});

		test('should cancel deletion when pressing Escape', async ({ page }) => {
			await storesPage.clickDeleteStore(createdStoreName);
			await confirmModal.expectOpen();
			await page.keyboard.press('Escape');
			await confirmModal.expectClosed();

			await storesPage.expectStoreVisible(createdStoreName);
		});
	});
});
