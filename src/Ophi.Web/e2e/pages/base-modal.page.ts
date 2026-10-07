import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class BaseModalPage extends BasePage {
	readonly modal: Locator;
	submitButton: Locator;
	cancelButton: Locator;

	constructor(page: Page, modalSelector: string, submitSelector: string = 'button[type="submit"]', cancelSelector: string = ':text("Cancel")') {
		super(page);
		this.modal = page.locator(modalSelector);
		this.submitButton = this.modal.locator(submitSelector);
		this.cancelButton = this.modal.locator(cancelSelector);
	}

	async expectOpen() {
		await expect(this.modal).toBeVisible({ timeout: 5000 });
	}

	async expectClosed() {
		await expect(this.modal).not.toBeVisible({ timeout: 5000 });
	}

	async submit() {
		await this.submitButton.click();
	}

	async cancel() {
		await this.cancelButton.click();
	}
}
