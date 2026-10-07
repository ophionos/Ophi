import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { TagsPage } from '../pages/tags.page';
import { TagModalPage } from '../pages/tag-modal.page';
import { ConfirmModalPage } from '../pages/confirm-modal.page';

test.describe('Tag Management', () => {
	let tagsPage: TagsPage;
	let tagModal: TagModalPage;
	let confirmModal: ConfirmModalPage;
	let api: TestApi;

	test.beforeEach(async ({ page }) => {
		tagsPage = new TagsPage(page);
		tagModal = new TagModalPage(page);
		confirmModal = new ConfirmModalPage(page, 'Delete Tag');
		api = new TestApi(page.request);
	});

	test.describe('View Tags', () => {
		test('should display tags page with stats', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await expect(tagsPage.pageTitle).toBeVisible();
			await expect(tagsPage.createButton).toBeVisible();
			await expect(tagsPage.totalTagsCount).toBeVisible();
			await expect(tagsPage.taggedProductsCount).toBeVisible();
		});

		test('should show empty state when no tags exist', async () => {
			// Clean up any existing test tags first
			const data = await api.getTags();
			for (const tag of data.items ?? []) {
				if (tag.name.startsWith('E2E')) {
					await api.deleteTag(tag.id);
				}
			}

			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			const count = await tagsPage.getTagCount();
			if (count === 0) {
				await expect(tagsPage.emptyState).toBeVisible();
			}
		});
	});

	test.describe('Create Tag', () => {
		test('should open create modal when clicking Create Tag', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();
			await tagModal.expectCreateMode();
		});

		test('should create a new tag', async ({ page }) => {
			const tagName = `E2E Tag ${Date.now()}`;

			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			const initialCount = await tagsPage.getTagCount();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();

			const responsePromise = page.waitForResponse('**/api/v1/tags');
			await tagModal.fillAndSubmit(tagName, '#EF4444');
			await responsePromise;

			await tagModal.expectClosed();
			await tagsPage.expectTagVisible(tagName);

			const newCount = await tagsPage.getTagCount();
			expect(newCount).toBe(initialCount + 1);

			// Cleanup
			const data = await api.getTags();
			const created = data.items.find((t) => t.name === tagName);
			if (created) {
				await api.deleteTag(created.id);
			}
		});

		test('should create tag with custom weight', async ({ page }) => {
			const tagName = `E2E Weighted Tag ${Date.now()}`;

			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();

			const responsePromise = page.waitForResponse('**/api/v1/tags');
			await tagModal.fillAndSubmit(tagName, '#22C55E', 10);
			await responsePromise;

			await tagModal.expectClosed();
			await tagsPage.expectTagVisible(tagName);

			// Cleanup
			const data = await api.getTags();
			const created = data.items.find((t) => t.name === tagName);
			if (created) {
				await api.deleteTag(created.id);
			}
		});

		test('should show validation error for empty name', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();
			await tagModal.submit();

			await tagModal.expectNameError('Name is required');
		});

		test('should close modal when clicking Cancel', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();
			await tagModal.cancel();
			await tagModal.expectClosed();
		});

		test('should close modal when pressing Escape', async ({ page }) => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();
			await page.keyboard.press('Escape');
			await tagModal.expectClosed();
		});

		test('should show color picker with preset colors', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			await tagModal.expectOpen();

			// Should have 10 preset color buttons
			const colorCount = await tagModal.colorButtons.count();
			expect(colorCount).toBe(10);
		});

		test('should show product count of zero for new tag', async ({ page }) => {
			const tagName = `E2E Tag ${Date.now()}`;

			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickCreate();
			const responsePromise = page.waitForResponse('**/api/v1/tags');
			await tagModal.fillAndSubmit(tagName);
			await responsePromise;

			await tagModal.expectClosed();
			await tagsPage.expectTagProductCount(tagName, 0);

			// Cleanup
			const data = await api.getTags();
			const created = data.items.find((t) => t.name === tagName);
			if (created) {
				await api.deleteTag(created.id);
			}
		});
	});

	test.describe('Edit Tag', () => {
		let testTagId: string;
		let testTagName: string;

		test.beforeEach(async () => {
			testTagName = `E2E Tag ${Date.now()}`;
			const tag = await api.createTag(testTagName, '#3B82F6', 0);
			testTagId = tag.id;
		});

		test.afterEach(async () => {
			if (testTagId) {
				try {
					await api.deleteTag(testTagId);
				} catch {
					// Already deleted
				}
			}
		});

		test('should open edit modal with existing data', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickEditTag(testTagName);
			await tagModal.expectOpen();
			await tagModal.expectEditMode();

			await expect(tagModal.nameInput).toHaveValue(testTagName);
		});

		test('should update tag name', async ({ page }) => {
			const updatedName = `E2E Renamed Tag ${Date.now()}`;

			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickEditTag(testTagName);
			await tagModal.expectOpen();

			await tagModal.nameInput.clear();
			await tagModal.fillName(updatedName);

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/tags/') && resp.request().method() === 'PUT'
			);
			await tagModal.submit();
			await responsePromise;

			await tagModal.expectClosed();
			await tagsPage.expectTagVisible(updatedName);
			await tagsPage.expectTagNotVisible(testTagName);
		});
	});

	test.describe('Delete Tag', () => {
		let testTagId: string;
		let testTagName: string;

		test.beforeEach(async () => {
			testTagName = `E2E Tag ${Date.now()}`;
			const tag = await api.createTag(testTagName, '#EF4444', 0);
			testTagId = tag.id;
		});

		test.afterEach(async () => {
			if (testTagId) {
				try {
					await api.deleteTag(testTagId);
				} catch {
					// Already deleted
				}
			}
		});

		test('should open delete confirmation modal', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickDeleteTag(testTagName);
			await confirmModal.expectOpen();
			await expect(confirmModal.title).toHaveText('Delete Tag');
		});

		test('should delete tag when confirmed', async ({ page }) => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			const initialCount = await tagsPage.getTagCount();

			await tagsPage.clickDeleteTag(testTagName);
			await confirmModal.expectOpen();

			const responsePromise = page.waitForResponse(
				(resp) => resp.url().includes('/api/v1/tags/') && resp.request().method() === 'DELETE'
			);
			await confirmModal.confirm();
			await responsePromise;

			await confirmModal.expectClosed();
			await tagsPage.expectTagNotVisible(testTagName);

			const newCount = await tagsPage.getTagCount();
			expect(newCount).toBe(initialCount - 1);

			testTagId = ''; // Already deleted
		});

		test('should cancel deletion when clicking Cancel', async () => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickDeleteTag(testTagName);
			await confirmModal.expectOpen();
			await confirmModal.cancel();
			await confirmModal.expectClosed();

			await tagsPage.expectTagVisible(testTagName);
		});

		test('should cancel deletion when pressing Escape', async ({ page }) => {
			await tagsPage.goto();
			await tagsPage.waitForLoaded();

			await tagsPage.clickDeleteTag(testTagName);
			await confirmModal.expectOpen();
			await page.keyboard.press('Escape');
			await confirmModal.expectClosed();

			await tagsPage.expectTagVisible(testTagName);
		});
	});
});
