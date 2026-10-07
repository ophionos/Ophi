import { Page, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class LoginPage extends BasePage {
	readonly emailInput;
	readonly passwordInput;
	readonly submitButton;
	readonly errorMessage;

	constructor(page: Page) {
		super(page);
		this.emailInput = page.getByLabel('Email address');
		// exact: the reveal toggle's aria-label ("Show password") also contains "Password".
		this.passwordInput = page.getByLabel('Password', { exact: true });
		this.submitButton = page.getByRole('button', { name: 'Sign in' });
		this.errorMessage = page.locator('.bg-red-50');
	}

	async goto() {
		await this.page.goto('/auth/login');
		// Wait for hydration before interacting (body[data-hydrated] is set by the
		// root layout's onMount) — a fill/click landing pre-hydration hits the
		// native form behavior instead of the JS submit handler.
		await this.page.locator('body[data-hydrated]').waitFor({ state: 'attached' });
	}

	async login(email: string, password: string) {
		await this.emailInput.fill(email);
		await this.passwordInput.fill(password);

		// Wait for the API response after clicking (ensures JS handles the submit)
		const responsePromise = this.page.waitForResponse(
			(resp) => resp.url().includes('/api/v1/auth/login'),
			{ timeout: 10000 }
		);
		await this.submitButton.click();
		await responsePromise;
	}

	async expectLoginSuccess() {
		await expect(this.page).toHaveURL(/\/dashboard/);
	}

	async expectLoginError(message?: string) {
		await expect(this.errorMessage).toBeVisible();
		if (message) {
			await expect(this.errorMessage).toContainText(message);
		}
	}
}
