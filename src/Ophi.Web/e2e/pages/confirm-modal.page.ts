import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class ConfirmModalPage extends BasePage {
	readonly modal: Locator;
	readonly title: Locator;
	readonly message: Locator;
	readonly confirmButton: Locator;
	readonly cancelButton: Locator;

	constructor(page: Page, filterText?: string) {
		super(page);
		// Only ConfirmModal labels its dialog with confirm-modal-title; a Modal.svelte dialog may be open below it.
		const baseModal = page.locator('[role="dialog"][aria-labelledby="confirm-modal-title"]');
		this.modal = filterText ? baseModal.filter({ hasText: filterText }) : baseModal;
		this.title = this.modal.locator('h3');
		this.message = this.modal.locator('p.text-sm.text-gray-600');
		this.confirmButton = this.modal.getByRole('button', { name: 'Delete' });
		this.cancelButton = this.modal.getByRole('button', { name: 'Cancel' });
	}

	async expectOpen() {
		await expect(this.modal).toBeVisible({ timeout: 5000 });
	}

	async expectClosed() {
		await expect(this.modal).not.toBeVisible({ timeout: 5000 });
	}

	async confirm() {
		await this.confirmButton.click();
	}

	async cancel() {
		await this.cancelButton.click();
	}
}
