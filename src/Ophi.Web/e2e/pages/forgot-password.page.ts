import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

export class ForgotPasswordPage extends BasePage {
	readonly heading: Locator;
	readonly emailInput: Locator;
	readonly submitButton: Locator;
	readonly successMessage: Locator;
	readonly errorMessage: Locator;
	readonly backToSignInLink: Locator;
	readonly signInLink: Locator;

	constructor(page: Page) {
		super(page);
		this.heading = page.getByRole('heading', { name: 'Reset your password' });
		this.emailInput = page.getByLabel('Email address');
		this.submitButton = page.getByRole('button', { name: 'Send reset link' });
		this.successMessage = page.locator('[data-testid="success-message"]');
		this.errorMessage = page.locator('.bg-red-50');
		this.backToSignInLink = page.getByRole('link', { name: 'Back to sign in' });
		this.signInLink = page.getByRole('link', { name: 'Sign in' });
	}

	async goto() {
		await this.page.goto('/auth/forgot-password');
		await this.waitForPageLoad();
	}

	async submitEmail(email: string) {
		await this.emailInput.fill(email);
		await this.submitButton.click();
	}

	async expectSuccess() {
		await expect(this.successMessage).toBeVisible({ timeout: 10000 });
	}

	async expectFormVisible() {
		await expect(this.emailInput).toBeVisible();
		await expect(this.submitButton).toBeVisible();
	}
}
