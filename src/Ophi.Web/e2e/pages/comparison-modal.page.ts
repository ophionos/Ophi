import { Page, Locator, expect } from '@playwright/test';
import { BaseModalPage } from './base-modal.page';

export class ComparisonModalPage extends BaseModalPage {
	readonly modalTitle: Locator;
	readonly nameInput: Locator;
	readonly descriptionInput: Locator;
	readonly errorMessage: Locator;

	constructor(page: Page) {
		super(page, '[role="dialog"]', 'button[type="submit"]');
		this.modalTitle = this.modal.locator('h2');
		// Scope form fields to the modal
		this.nameInput = this.modal.locator('#name');
		this.descriptionInput = this.modal.locator('textarea');
		this.errorMessage = this.modal.locator('.bg-red-50');
	}

	async expectCreateMode() {
		await expect(this.modalTitle).toHaveText('Create Comparison Group');
	}

	async fillForm(name: string, description?: string) {
		await this.nameInput.fill(name);
		if (description) {
			await this.descriptionInput.fill(description);
		}
	}

	async fillAndSubmit(name: string, description?: string) {
		await this.fillForm(name, description);
		await this.submit();
	}
}
