import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { ApiKeysPage } from '../pages/api-keys.page';

// The prefix-matched cleanup in before/afterEach deletes every `E2E Key*` on the
// shared TEST_USER — under `fullyParallel` it would delete a parallel sibling's
// key mid-test. Run the file sequentially.
test.describe.configure({ mode: 'default' });

const PREFIX = 'E2E Key';

async function cleanup(api: TestApi) {
	const keys = await api.listApiKeys();
	for (const k of keys ?? []) {
		if (k.name.startsWith(PREFIX)) {
			await api.deleteApiKey(k.id);
		}
	}
}

test.describe('API Keys', () => {
	let apiKeys: ApiKeysPage;
	let api: TestApi;

	test.beforeEach(async ({ page }) => {
		apiKeys = new ApiKeysPage(page);
		api = new TestApi(page.request);
		await cleanup(api);
	});

	test.afterEach(async () => {
		await cleanup(api);
	});

	test('should show the create button and helper text', async () => {
		await apiKeys.goto();
		await expect(apiKeys.createButton).toBeVisible();
		await expect(apiKeys.page.getByText('Authenticate scripts')).toBeVisible();
	});

	test('should create a key and reveal the secret once', async ({ page }) => {
		const name = `${PREFIX} ${Date.now()}`;
		await apiKeys.goto();
		await apiKeys.openCreate();
		await apiKeys.fillForm(name);

		const created = page.waitForResponse(
			(r) => r.url().includes('/api/v1/api-keys') && r.request().method() === 'POST'
		);
		await apiKeys.submit();
		await created;

		// The secret is revealed once, with the warning and a copy affordance.
		await expect(apiKeys.createdKey).toBeVisible();
		await expect(apiKeys.createdKey).toContainText('ophi_');
		await expect(page.getByText(/will not be shown again/i)).toBeVisible();
		await expect(apiKeys.copyButton).toBeVisible();

		await apiKeys.doneButton.click();

		// Now listed (read scope by default).
		const item = apiKeys.keyItem(name);
		await expect(item).toBeVisible();
		await expect(item.getByText('read', { exact: true })).toBeVisible();
	});

	test('should require a name', async () => {
		await apiKeys.goto();
		await apiKeys.openCreate();
		await apiKeys.submit();
		await expect(apiKeys.page.getByText('Name is required')).toBeVisible();
	});

	test('should require at least one scope', async () => {
		await apiKeys.goto();
		await apiKeys.openCreate();
		await apiKeys.fillForm(`${PREFIX} ${Date.now()}`, { read: false });
		await apiKeys.submit();
		await expect(apiKeys.page.getByText('Select at least one scope')).toBeVisible();
	});

	test('should create a key with read and write scopes', async () => {
		const name = `${PREFIX} write ${Date.now()}`;
		await apiKeys.goto();
		await apiKeys.openCreate();
		await apiKeys.fillForm(name, { write: true });
		await apiKeys.submit();

		await expect(apiKeys.createdKey).toBeVisible();
		await apiKeys.doneButton.click();

		const item = apiKeys.keyItem(name);
		await expect(item.getByText('read', { exact: true })).toBeVisible();
		await expect(item.getByText('write', { exact: true })).toBeVisible();
	});

	test('should revoke a key', async ({ page }) => {
		const name = `${PREFIX} revoke ${Date.now()}`;
		await api.createApiKey(name);

		await apiKeys.goto();
		await expect(apiKeys.keyItem(name)).toBeVisible();

		await apiKeys.revoke(name);
		const deleted = page.waitForResponse(
			(r) => r.url().includes('/api/v1/api-keys/') && r.request().method() === 'DELETE'
		);
		await apiKeys.revokeConfirm.click();
		await deleted;

		await expect(apiKeys.keyItem(name)).not.toBeVisible();
	});

	test('should keep the key when revoke is cancelled', async () => {
		const name = `${PREFIX} cancel ${Date.now()}`;
		await api.createApiKey(name);

		await apiKeys.goto();
		await apiKeys.revoke(name);
		await apiKeys.revokeCancel.click();

		await expect(apiKeys.confirmModal).not.toBeVisible();
		await expect(apiKeys.keyItem(name)).toBeVisible();
	});
});
