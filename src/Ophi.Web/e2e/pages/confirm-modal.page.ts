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
		// Find the modal - it's a div inside the fixed overlay that contains the alert icon
		const baseModal = page.locator('.fixed.inset-0 .bg-white.rounded-lg.shadow-xl');
		this.modal = filterText
			? baseModal.filter({ hasText: filterText })
			: baseModal.filter({ has: page.locator('svg.lucide-alert-triangle') });
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
