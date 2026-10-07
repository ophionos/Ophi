import { test, expect } from '@playwright/test';
import { ForgotPasswordPage } from '../pages/forgot-password.page';
import { ResetPasswordPage } from '../pages/reset-password.page';
import { LoginPage } from '../pages/login.page';

test.describe('Password Reset Flow', () => {
	// These tests don't require authentication — use fresh contexts
	test.use({ storageState: { cookies: [], origins: [] } });

	test.describe('Forgot Password Page', () => {
		let forgotPage: ForgotPasswordPage;

		test.beforeEach(async ({ page }) => {
			forgotPage = new ForgotPasswordPage(page);
		});

		test('should display forgot password form', async ({ page }) => {
			await forgotPage.goto();

			await expect(forgotPage.heading).toBeVisible();
			await forgotPage.expectFormVisible();
			await expect(forgotPage.signInLink).toBeVisible();
		});

		test('should show success message after submitting email', async ({ page }) => {
			await forgotPage.goto();

			// The API always returns success for email enumeration protection
			const responsePromise = page.waitForResponse('**/api/v1/auth/forgot-password');
			await forgotPage.submitEmail('test@example.com');
			await responsePromise;

			await forgotPage.expectSuccess();
			await expect(forgotPage.successMessage).toContainText('password reset link has been sent');
		});

		test('should show Back to sign in link after success', async ({ page }) => {
			await forgotPage.goto();

			const responsePromise = page.waitForResponse('**/api/v1/auth/forgot-password');
			await forgotPage.submitEmail('test@example.com');
			await responsePromise;

			await forgotPage.expectSuccess();
			await expect(forgotPage.backToSignInLink).toBeVisible();
		});

		test('should navigate to login page via Sign in link', async ({ page }) => {
			await forgotPage.goto();

			await forgotPage.signInLink.click();
			await expect(page).toHaveURL(/\/auth\/login/);
		});

		test('should navigate to login page via Back to sign in link after success', async ({ page }) => {
			await forgotPage.goto();

			const responsePromise = page.waitForResponse('**/api/v1/auth/forgot-password');
			await forgotPage.submitEmail('test@example.com');
			await responsePromise;

			await forgotPage.expectSuccess();
			await forgotPage.backToSignInLink.click();
			await expect(page).toHaveURL(/\/auth\/login/);
		});

		test('should hide form after successful submission', async ({ page }) => {
			await forgotPage.goto();

			const responsePromise = page.waitForResponse('**/api/v1/auth/forgot-password');
			await forgotPage.submitEmail('test@example.com');
			await responsePromise;

			await forgotPage.expectSuccess();
			await expect(forgotPage.emailInput).not.toBeVisible();
			await expect(forgotPage.submitButton).not.toBeVisible();
		});
	});

	test.describe('Reset Password Page', () => {
		let resetPage: ResetPasswordPage;

		test.beforeEach(async ({ page }) => {
			resetPage = new ResetPasswordPage(page);
		});

		test('should display reset password form', async ({ page }) => {
			await resetPage.goto('dummy-token');

			await expect(resetPage.heading).toBeVisible();
			await resetPage.expectFormVisible();
			await expect(resetPage.signInLink).toBeVisible();
		});

		test('should show error when passwords do not match', async ({ page }) => {
			await resetPage.goto('dummy-token');

			await resetPage.fillAndSubmit('NewPassword123!', 'DifferentPassword123!');

			await resetPage.expectError('Passwords do not match');
		});

		test('should show error when token is missing', async ({ page }) => {
			await resetPage.goto(); // No token

			await resetPage.fillAndSubmit('NewPassword123!', 'NewPassword123!');

			await resetPage.expectError('Missing reset token');
		});

		test('should navigate to login page via Sign in link', async ({ page }) => {
			await resetPage.goto('dummy-token');

			await resetPage.signInLink.click();
			await expect(page).toHaveURL(/\/auth\/login/);
		});
	});

	test.describe('Navigation between auth pages', () => {
		test('should navigate from login to forgot password', async ({ page }) => {
			const loginPage = new LoginPage(page);
			await loginPage.goto();

			const forgotLink = page.getByRole('link', { name: 'Forgot your password?' });
			await expect(forgotLink).toBeVisible();
			await forgotLink.click();

			await expect(page).toHaveURL(/\/auth\/forgot-password/);
		});

		test('should navigate from forgot password to login and back', async ({ page }) => {
			const forgotPage = new ForgotPasswordPage(page);
			await forgotPage.goto();

			await forgotPage.signInLink.click();
			await expect(page).toHaveURL(/\/auth\/login/);

			const forgotLink = page.getByRole('link', { name: 'Forgot your password?' });
			await forgotLink.click();
			await expect(page).toHaveURL(/\/auth\/forgot-password/);
		});
	});
});
