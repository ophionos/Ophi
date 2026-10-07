import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class AlertModalPage extends BasePage {
	readonly modal: Locator;
	readonly title: Locator;
	readonly closeButton: Locator;
	readonly conditionRadios: Locator;
	readonly targetPriceInput: Locator;
	readonly submitButton: Locator;
	readonly cancelButton: Locator;
	readonly errorMessage: Locator;

	constructor(page: Page) {
		super(page);
		this.modal = page.locator('.fixed.inset-0.z-50');
		this.title = page.getByRole('heading', { name: 'Create Price Alert' });
		this.closeButton = this.modal.locator('button').filter({ has: page.locator('svg.lucide-x') });
		this.conditionRadios = this.modal.locator('input[name="condition"]');
		this.targetPriceInput = this.modal.locator('#targetPrice');
		// Submit button is inside the modal form, scope it to the modal
		this.submitButton = this.modal.getByRole('button', { name: 'Create Alert' });
		this.cancelButton = this.modal.getByRole('button', { name: 'Cancel' });
		this.errorMessage = this.modal.locator('.bg-red-50');
	}

	async expectOpen() {
		await expect(this.title).toBeVisible({ timeout: 5000 });
	}

	async expectClosed() {
		await expect(this.title).not.toBeVisible({ timeout: 5000 });
	}

	async selectCondition(condition: 'below' | 'above' | 'percentdrop') {
		// Use modal-scoped locator for reliable selection
		await this.modal.locator(`input[name="condition"][value="${condition}"]`).check();
	}

	async fillTargetPrice(price: number) {
		await this.targetPriceInput.clear();
		await this.targetPriceInput.fill(price.toString());
	}

	async submit() {
		await this.submitButton.click();
	}

	async cancel() {
		await this.cancelButton.click();
	}

	async close() {
		await this.closeButton.click();
	}

	async fillAndSubmit(targetPrice: number, condition: 'below' | 'above' | 'percentdrop' = 'below') {
		await this.selectCondition(condition);
		await this.fillTargetPrice(targetPrice);
		await this.submit();
	}

	async expectError(message: string) {
		await expect(this.errorMessage).toContainText(message);
	}
}
