import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class AddProductModalPage extends BasePage {
	readonly modal: Locator;
	readonly modalTitle: Locator;
	readonly productButtons: Locator;
	readonly addButton: Locator;
	readonly cancelButton: Locator;
	readonly emptyState: Locator;

	constructor(page: Page) {
		super(page);
		this.modal = page.locator('.fixed.inset-0.z-50');
		// The modal title is "Add Products" (plural) — distinct from the "Add Product" trigger.
		this.modalTitle = page.getByRole('heading', { name: 'Add Products' });
		this.productButtons = this.modal.locator('button').filter({
			has: page.locator('.truncate')
		});
		// Footer submit reads "Add" or "Add (N)" once products are selected.
		this.addButton = this.modal.getByRole('button', { name: /^Add( \(\d+\))?$/ });
		this.cancelButton = this.modal.getByRole('button', { name: 'Cancel' });
		this.emptyState = page.getByText('No products available');
	}

	async expectOpen() {
		await expect(this.modalTitle).toBeVisible({ timeout: 5000 });
	}

	async expectClosed() {
		await expect(this.modalTitle).not.toBeVisible({ timeout: 5000 });
	}

	async selectProduct(productName: string) {
		const productBtn = this.productButtons.filter({ hasText: productName });
		await productBtn.click();
	}

	async addSelectedProduct() {
		await this.addButton.click();
	}

	async selectAndAdd(productName: string) {
		await this.selectProduct(productName);
		await this.addSelectedProduct();
	}

	async cancel() {
		await this.cancelButton.click();
	}

	async expectProductAvailable(productName: string) {
		await expect(this.productButtons.filter({ hasText: productName })).toBeVisible();
	}

	async expectNoProductsAvailable() {
		await expect(this.emptyState).toBeVisible();
	}
}
