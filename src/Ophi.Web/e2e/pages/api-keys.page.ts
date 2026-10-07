import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Page Object for /settings/api-keys. API keys are per-user and non-destructive,
 * so these tests can run as the shared TEST_USER (clean up via API between tests).
 */
export class ApiKeysPage extends BasePage {
	readonly createButton: Locator;
	readonly keyList: Locator;
	readonly emptyState: Locator;

	// Create modal
	readonly nameInput: Locator;
	readonly readScope: Locator;
	readonly writeScope: Locator;
	readonly expiresSelect: Locator;
	readonly createSubmit: Locator;
	readonly modalError: Locator;
	readonly createdKey: Locator;
	readonly copyButton: Locator;
	readonly doneButton: Locator;

	// Revoke confirmation
	readonly confirmModal: Locator;
	readonly revokeConfirm: Locator;
	readonly revokeCancel: Locator;

	constructor(page: Page) {
		super(page);
		this.createButton = page.getByRole('button', { name: 'Create', exact: true });
		this.keyList = page.locator('ul li');
		this.emptyState = page.getByText('No API keys created');

		this.nameInput = page.locator('#key-name');
		this.readScope = page.getByTestId('scope-read');
		this.writeScope = page.getByTestId('scope-write');
		this.expiresSelect = page.locator('#key-expires');
		this.createSubmit = page.getByRole('button', { name: 'Create Key' });
		this.modalError = page.locator('.bg-red-50, .dark\\:bg-red-900\\/30').first();
		this.createdKey = page.locator('code.select-all');
		this.copyButton = page.getByRole('button', { name: 'Copy to clipboard' });
		this.doneButton = page.getByRole('button', { name: 'Done' });

		this.confirmModal = page.getByRole('dialog').filter({ hasText: 'Revoke API Key' });
		this.revokeConfirm = this.confirmModal.getByRole('button', { name: 'Revoke' });
		this.revokeCancel = this.confirmModal.getByRole('button', { name: 'Cancel' });
	}

	async goto() {
		await this.page.goto('/settings/api-keys', { waitUntil: 'domcontentloaded' });
		await expect(this.createButton).toBeVisible();
	}

	keyItem(name: string): Locator {
		return this.keyList.filter({ hasText: name });
	}

	async openCreate() {
		// Retry the click until the modal opens — a click before hydration is a no-op.
		await expect(async () => {
			await this.createButton.click();
			await expect(this.nameInput).toBeVisible({ timeout: 1000 });
		}).toPass({ timeout: 15000 });
	}

	async fillForm(name: string, opts: { read?: boolean; write?: boolean } = {}) {
		await this.nameInput.fill(name);
		if (opts.read === false) await this.readScope.uncheck();
		if (opts.write === true) await this.writeScope.check();
	}

	async submit() {
		await this.createSubmit.click();
	}

	async revoke(name: string) {
		// Retry like openCreate: the key list is server-rendered, so the row can be visible
		// before hydration attaches the click handler.
		await expect(async () => {
			await this.keyItem(name).getByRole('button', { name: 'Revoke API key' }).click();
			await expect(this.confirmModal).toBeVisible({ timeout: 1000 });
		}).toPass({ timeout: 15000 });
	}
}
