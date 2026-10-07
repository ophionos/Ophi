import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Page Object for /settings/notifications — Discord channel settings plus
 * outbound webhook-target CRUD. Interactions are wrapped to survive the
 * hydration window (one-way-bound inputs reset from state on mount).
 */
export class NotificationsSettingsPage extends BasePage {
	readonly discordToggle: Locator;
	readonly discordWebhookUrl: Locator;
	readonly discordTestButton: Locator;

	readonly addWebhookButton: Locator;
	readonly webhookItems: Locator;

	// Webhook modal
	readonly webhookName: Locator;
	readonly webhookUrl: Locator;
	readonly webhookSubmit: Locator;

	// Delete confirmation
	readonly confirmModal: Locator;
	readonly deleteConfirm: Locator;
	readonly deleteCancel: Locator;

	constructor(page: Page) {
		super(page);
		this.discordToggle = page.getByTestId('discord-toggle');
		this.discordWebhookUrl = page.getByTestId('discord-webhook-url');
		this.discordTestButton = page.getByTestId('discord-test-button');

		this.addWebhookButton = page.getByRole('button', { name: 'Add', exact: true });
		this.webhookItems = page.locator('ul li');

		this.webhookName = page.locator('#webhook-name');
		this.webhookUrl = page.locator('#webhook-url');
		this.webhookSubmit = page.getByRole('button', { name: 'Add Webhook' });

		this.confirmModal = page.getByRole('dialog').filter({ hasText: 'Delete Webhook' });
		this.deleteConfirm = this.confirmModal.getByRole('button', { name: 'Delete' });
		this.deleteCancel = this.confirmModal.getByRole('button', { name: 'Cancel' });
	}

	async goto() {
		await this.page.goto('/settings/notifications', { waitUntil: 'domcontentloaded' });
		await expect(this.discordToggle).toBeVisible();
	}

	private settingsPut(timeout: number) {
		return this.page.waitForResponse(
			(r) => r.url().includes('/api/v1/settings') && r.request().method() === 'PUT',
			{ timeout }
		);
	}

	async toggleDiscord() {
		await expect(async () => {
			const pending = this.settingsPut(3000);
			await this.discordToggle.click();
			await pending;
		}).toPass({ timeout: 15000 });
	}

	/** Sets the Discord webhook URL (one-way bound + on-mount $effect → fill can be clobbered). */
	async setDiscordUrl(url: string) {
		await expect(async () => {
			await this.discordWebhookUrl.fill(url);
			await this.page.waitForTimeout(300);
			await expect(this.discordWebhookUrl).toHaveValue(url, { timeout: 500 });
			const pending = this.settingsPut(3000);
			await this.discordWebhookUrl.blur();
			await pending;
		}).toPass({ timeout: 20000 });
	}

	webhookItem(name: string): Locator {
		return this.webhookItems.filter({ hasText: name });
	}

	async openAddWebhook() {
		await expect(async () => {
			await this.addWebhookButton.click();
			await expect(this.webhookName).toBeVisible({ timeout: 1000 });
		}).toPass({ timeout: 15000 });
	}

	async fillWebhook(name: string, url: string) {
		await this.webhookName.fill(name);
		await this.webhookUrl.fill(url);
	}

	async deleteWebhook(name: string) {
		await this.webhookItem(name).getByRole('button', { name: 'Delete webhook' }).click();
		await expect(this.confirmModal).toBeVisible();
	}
}
