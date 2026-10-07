import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class ResetPasswordPage extends BasePage {
	readonly heading: Locator;
	readonly newPasswordInput: Locator;
	readonly confirmPasswordInput: Locator;
	readonly submitButton: Locator;
	readonly successMessage: Locator;
	readonly errorMessage: Locator;
	readonly signInLink: Locator;

	constructor(page: Page) {
		super(page);
		this.heading = page.getByRole('heading', { name: 'Set new password' });
		this.newPasswordInput = page.getByLabel('New password');
		this.confirmPasswordInput = page.getByLabel('Confirm password');
		this.submitButton = page.getByRole('button', { name: 'Reset password' });
		this.successMessage = page.locator('[data-testid="success-message"]');
		this.errorMessage = page.locator('.bg-red-50');
		this.signInLink = page.getByRole('link', { name: 'Sign in' });
	}

	async goto(token?: string) {
		const url = token ? `/auth/reset-password?token=${token}` : '/auth/reset-password';
		await this.page.goto(url);
		await this.waitForPageLoad();
	}

	async fillPasswords(password: string, confirmPassword: string) {
		await this.newPasswordInput.fill(password);
		await this.confirmPasswordInput.fill(confirmPassword);
	}

	async submit() {
		await this.submitButton.click();
	}

	async fillAndSubmit(password: string, confirmPassword: string) {
		await this.fillPasswords(password, confirmPassword);
		await this.submit();
	}

	async expectFormVisible() {
		await expect(this.newPasswordInput).toBeVisible();
		await expect(this.confirmPasswordInput).toBeVisible();
		await expect(this.submitButton).toBeVisible();
	}

	async expectError(message: string) {
		await expect(this.errorMessage).toContainText(message);
	}
}
