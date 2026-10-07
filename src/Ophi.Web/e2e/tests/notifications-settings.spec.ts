import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { NotificationsSettingsPage } from '../pages/notifications-settings.page';

// These tests read-modify-restore the shared TEST_USER's settings row, and the
// prefix-matched webhook cleanup in before/afterEach would delete a parallel
// sibling's webhook mid-test under `fullyParallel`. Run the file sequentially.
test.describe.configure({ mode: 'default' });

const PREFIX = 'E2E Hook';

async function cleanupWebhooks(api: TestApi) {
	const hooks = await api.listWebhooks();
	for (const h of hooks ?? []) {
		if (h.name.startsWith(PREFIX)) {
			await api.deleteWebhook(h.id);
		}
	}
}

test.describe('Notification Settings', () => {
	let notifications: NotificationsSettingsPage;
	let api: TestApi;
	let original: Record<string, unknown> | undefined;

	test.beforeEach(async ({ page }) => {
		notifications = new NotificationsSettingsPage(page);
		api = new TestApi(page.request);
		original ??= await api.getSettings();
		await cleanupWebhooks(api);
	});

	test.afterEach(async () => {
		await cleanupWebhooks(api);
		if (original) {
			await api.updateSettings({
				discordNotificationsEnabled: original.discordNotificationsEnabled,
				discordWebhookUrl: ''
			});
		}
	});

	test.describe('Discord', () => {
		test('should toggle and persist Discord notifications', async () => {
			await notifications.goto();
			const before = await notifications.discordToggle.getAttribute('aria-checked');
			const after = before === 'true' ? 'false' : 'true';

			await notifications.toggleDiscord();
			await expect(notifications.discordToggle).toHaveAttribute('aria-checked', after);

			await notifications.page.reload();
			await expect(notifications.discordToggle).toHaveAttribute('aria-checked', after);
		});

		test('should save a webhook URL and enable the Test button', async () => {
			await notifications.goto();
			await expect(notifications.discordTestButton).toBeDisabled();

			await notifications.setDiscordUrl(
				'https://discord.com/api/webhooks/123456789012345678/e2e-test-token-abcDEF'
			);

			await expect(notifications.discordTestButton).toBeEnabled();
			await expect(notifications.discordWebhookUrl).toHaveAttribute('placeholder', /configured/i);

			// Durable: the server records a configured webhook.
			const settings = await api.getSettings();
			expect(settings.discordWebhookConfigured).toBeTruthy();
		});
	});

	test.describe('Outbound webhooks', () => {
		test('should add a webhook target', async ({ page }) => {
			const name = `${PREFIX} ${Date.now()}`;
			await notifications.goto();
			await notifications.openAddWebhook();
			await notifications.fillWebhook(name, 'https://example.com/e2e-hook');

			const created = page.waitForResponse(
				(r) => r.url().includes('/api/v1/webhooks') && r.request().method() === 'POST'
			);
			await notifications.webhookSubmit.click();
			await created;

			await expect(notifications.webhookItem(name)).toBeVisible();
		});

		test('should delete a webhook target', async ({ page }) => {
			const name = `${PREFIX} del ${Date.now()}`;
			await api.post('/webhooks', {
				name,
				url: 'https://example.com/e2e-hook-del',
				events: ['alert_fired'],
				isEnabled: true
			});

			await notifications.goto();
			await expect(notifications.webhookItem(name)).toBeVisible();

			await notifications.deleteWebhook(name);
			const deleted = page.waitForResponse(
				(r) => r.url().includes('/api/v1/webhooks/') && r.request().method() === 'DELETE'
			);
			await notifications.deleteConfirm.click();
			await deleted;

			await expect(notifications.webhookItem(name)).not.toBeVisible();
		});

		test('should keep the webhook when delete is cancelled', async () => {
			const name = `${PREFIX} keep ${Date.now()}`;
			await api.post('/webhooks', {
				name,
				url: 'https://example.com/e2e-hook-keep',
				events: ['alert_fired'],
				isEnabled: true
			});

			await notifications.goto();
			await notifications.deleteWebhook(name);
			await notifications.deleteCancel.click();

			await expect(notifications.confirmModal).not.toBeVisible();
			await expect(notifications.webhookItem(name)).toBeVisible();
		});
	});
});
