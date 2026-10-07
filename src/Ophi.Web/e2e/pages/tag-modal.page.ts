import { Page, Locator, expect } from '@playwright/test';
import { BaseModalPage } from './base-modal.page';

export class TagModalPage extends BaseModalPage {
	readonly modalTitle: Locator;
	readonly nameInput: Locator;
	readonly weightInput: Locator;
	readonly errorMessage: Locator;
	readonly nameError: Locator;
	readonly colorButtons: Locator;

	constructor(page: Page) {
		super(page, '.fixed.inset-0.z-50 .bg-white.rounded-lg.shadow-xl', '[data-testid="tag-save-button"]');
		this.modalTitle = this.modal.locator('h2');
		this.nameInput = page.locator('[data-testid="tag-name-input"]');
		this.weightInput = page.locator('[data-testid="tag-weight-input"]');
		this.cancelButton = this.modal.getByRole('button', { name: 'Cancel' });
		this.errorMessage = this.modal.locator('.bg-red-50');
		this.nameError = this.modal.locator('.text-red-600, .text-red-400');
		this.colorButtons = this.modal.locator('button[aria-label^="Select color"]');
	}

	async expectCreateMode() {
		await expect(this.modalTitle).toHaveText('Create Tag');
	}

	async expectEditMode() {
		await expect(this.modalTitle).toHaveText('Edit Tag');
	}

	async fillName(name: string) {
		await this.nameInput.fill(name);
	}

	async fillWeight(weight: number) {
		await this.weightInput.fill(weight.toString());
	}

	async selectPresetColor(colorHex: string) {
		await this.modal.locator(`button[aria-label="Select color ${colorHex}"]`).click();
	}

	async fillAndSubmit(name: string, color?: string, weight?: number) {
		await this.fillName(name);
		if (color) {
			await this.selectPresetColor(color);
		}
		if (weight !== undefined) {
			await this.fillWeight(weight);
		}
		await this.submit();
	}

	async expectNameError(message: string) {
		await expect(this.nameError).toContainText(message);
	}
}
