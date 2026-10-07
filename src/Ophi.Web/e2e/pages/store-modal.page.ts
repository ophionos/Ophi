import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';
import type { TestStoreData } from '../fixtures/test-data';

export class StoreModalPage extends BasePage {
	readonly modal: Locator;
	readonly modalTitle: Locator;
	readonly storeIdInput: Locator;
	readonly nameInput: Locator;
	readonly priceLocaleSelect: Locator;
	readonly requiresJsCheckbox: Locator;
	readonly submitButton: Locator;
	readonly cancelButton: Locator;
	readonly closeButton: Locator;
	readonly errorMessage: Locator;

	constructor(page: Page) {
		super(page);
		this.modal = page.locator('.fixed.inset-0 .bg-white.rounded-lg.shadow-xl');
		this.modalTitle = this.modal.locator('h2');
		this.storeIdInput = page.locator('#storeId');
		this.nameInput = page.locator('#name');
		this.priceLocaleSelect = page.locator('[data-testid="store-price-locale"]');
		this.requiresJsCheckbox = page.locator('[data-testid="store-requires-js"]');
		this.submitButton = page.getByRole('button', {
			name: /Create Store|Update Store/
		});
		this.cancelButton = page.getByRole('button', { name: 'Cancel' });
		this.closeButton = this.modal
			.locator('button')
			.filter({ has: page.locator('svg') })
			.first();
		this.errorMessage = this.modal.locator('.bg-red-50');
	}

	async expectOpen() {
		await expect(this.modal).toBeVisible({ timeout: 5000 });
	}

	async expectClosed() {
		await expect(this.modal).not.toBeVisible({ timeout: 5000 });
	}

	async fillForm(data: TestStoreData, isEdit = false) {
		// Fill store ID only for create mode
		if (!isEdit && data.storeId) {
			await this.storeIdInput.fill(data.storeId);
		}

		// Fill display name
		await this.nameInput.fill(data.name);

		// Fill domain patterns
		await this.fillSelectorList('Domains', data.domainPatterns);

		// Fill scraping options
		if (data.priceLocale) {
			await this.priceLocaleSelect.selectOption(data.priceLocale);
		}
		if (data.requiresJavaScript) {
			await this.requiresJsCheckbox.check();
		}

		// Fill CSS selectors
		await this.fillSelectorList('Price Selectors', data.priceSelectors);
		await this.fillSelectorList('Name Selectors', data.nameSelectors);
		await this.fillSelectorList('Image Selectors', data.imageSelectors);

		// Fill optional regex patterns if provided
		if (data.priceRegexPatterns?.length) {
			await this.fillSelectorList('Price Regex Patterns', data.priceRegexPatterns);
		}
		if (data.imageRegexPatterns?.length) {
			await this.fillSelectorList('Image Regex Patterns', data.imageRegexPatterns);
		}
	}

	private async fillSelectorList(label: string, values: string[]) {
		// Find the section by label text - need to go up to the outer container
		// Structure: div.space-y-2 > div (header with label) > span (label text)
		const section = this.page.locator('.space-y-2').filter({ hasText: label });
		const inputs = section.locator('input[type="text"]');

		for (let i = 0; i < values.length; i++) {
			const inputCount = await inputs.count();

			// Add new input if needed
			if (i >= inputCount) {
				const addButton = section.getByText('Add');
				await addButton.click();
			}

			await inputs.nth(i).fill(values[i]);
		}
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

	async expectCreateMode() {
		await expect(this.modalTitle).toHaveText('Create Store Configuration');
		await expect(this.storeIdInput).toBeVisible();
	}

	async expectEditMode() {
		await expect(this.modalTitle).toHaveText('Edit Store Configuration');
		await expect(this.storeIdInput).not.toBeVisible();
	}

	async expectValidationError(message: string) {
		const errorText = this.page.getByText(message);
		await expect(errorText).toBeVisible();
	}
}
