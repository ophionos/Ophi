import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class RegisterPage extends BasePage {
	readonly nameInput: Locator;
	readonly emailInput: Locator;
	readonly passwordInput: Locator;
	readonly confirmPasswordInput: Locator;
	readonly submitButton: Locator;
	readonly errorMessage: Locator;
	readonly loginLink: Locator;

	constructor(page: Page) {
		super(page);
		this.nameInput = page.getByLabel('Name');
		this.emailInput = page.getByLabel('Email address');
		this.passwordInput = page.getByLabel('Password', { exact: true });
		this.confirmPasswordInput = page.getByLabel('Confirm Password');
		this.submitButton = page.getByRole('button', { name: 'Create account' });
		this.errorMessage = page.locator('.bg-red-50');
		this.loginLink = page.getByRole('link', { name: 'Sign in' });
	}

	async goto() {
		await this.page.goto('/auth/register');
		// Wait for hydration (see login.page.ts) — pre-hydration fills/clicks hit
		// the native GET form submit and no register POST ever happens.
		await this.page.locator('body[data-hydrated]').waitFor({ state: 'attached' });
	}

	async register(name: string, email: string, password: string, confirmPassword?: string) {
		await this.nameInput.fill(name);
		await this.emailInput.fill(email);
		await this.passwordInput.fill(password);
		await this.confirmPasswordInput.fill(confirmPassword ?? password);

		const responsePromise = this.page.waitForResponse(
			(resp) => resp.url().includes('/api/v1/auth/register'),
			{ timeout: 10000 }
		);
		await this.submitButton.click();

		try {
			await responsePromise;
		} catch {
			// Response may not happen if client-side validation fails
		}
	}

	async expectRegistrationSuccess() {
		await expect(this.page).toHaveURL(/\/dashboard/, { timeout: 10000 });
	}

	async expectRegistrationError(message?: string) {
		await expect(this.errorMessage).toBeVisible();
		if (message) {
			await expect(this.errorMessage).toContainText(message);
		}
	}

	async expectFieldError(field: string, message: string) {
		// Each field's error list is `#{field}-errors` (the input's aria-describedby target) — not
		// a DOM-position lookup, which broke when the password inputs gained a toggle wrapper.
		await expect(this.page.locator(`#${field}-errors`)).toContainText(message);
	}

	async clickLoginLink() {
		await this.loginLink.click();
		await expect(this.page).toHaveURL(/\/auth\/login/);
	}
}
