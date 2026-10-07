import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/login.page';
import { RegisterPage } from '../pages/register.page';
import { DashboardPage } from '../pages/dashboard.page';
import { StoresPage } from '../pages/stores.page';
import { TEST_USER } from '../fixtures/auth.fixture';

test.describe('Authentication', () => {
	test.describe('Registration', () => {
		test('should register a new user successfully', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const registerPage = new RegisterPage(page);
			const dashboardPage = new DashboardPage(page);

			// Generate unique email for this test
			const uniqueEmail = `test-${Date.now()}@example.com`;
			const testName = 'Test User';
			const testPassword = 'TestPassword123!';

			await registerPage.goto();
			await registerPage.register(testName, uniqueEmail, testPassword);
			await registerPage.expectRegistrationSuccess();

			// Verify we're on dashboard and logged in
			await dashboardPage.waitForDashboardLoaded();
			await dashboardPage.expectLoggedInAs(testName);

			await context.close();
		});

		test('should show error when passwords do not match', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const registerPage = new RegisterPage(page);

			await registerPage.goto();
			await registerPage.nameInput.fill('Test User');
			await registerPage.emailInput.fill('test@example.com');
			await registerPage.passwordInput.fill('Password123!');
			await registerPage.confirmPasswordInput.fill('DifferentPassword123!');
			await registerPage.submitButton.click();

			// Should show client-side validation error
			await registerPage.expectFieldError('confirmPassword', 'Passwords do not match');

			// Should still be on register page
			await expect(page).toHaveURL(/\/auth\/register/);

			await context.close();
		});

		test('should show error when email already exists', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const registerPage = new RegisterPage(page);

			await registerPage.goto();
			// Try to register with the existing test user email
			await registerPage.register(TEST_USER.name, TEST_USER.email, TEST_USER.password);

			// Should show error about duplicate email
			await registerPage.expectRegistrationError();

			await context.close();
		});

		test('should navigate to login page via link', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const registerPage = new RegisterPage(page);

			await registerPage.goto();
			await registerPage.clickLoginLink();

			await expect(page).toHaveURL(/\/auth\/login/);

			await context.close();
		});
	});

	test.describe('Login', () => {
		test('should login with valid credentials', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);

			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();

			// Verify logged in state
			await dashboardPage.waitForDashboardLoaded();
			await dashboardPage.expectLoggedInAs(TEST_USER.name);
			await dashboardPage.expectNavVisible();

			await context.close();
		});

		test('should show error for invalid credentials', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);

			await loginPage.goto();
			await loginPage.login('invalid@example.com', 'wrongpassword');
			await loginPage.expectLoginError();

			// Should still be on login page
			await expect(page).toHaveURL(/\/auth\/login/);

			await context.close();
		});

		test('should show error for wrong password', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);

			await loginPage.goto();
			await loginPage.login(TEST_USER.email, 'WrongPassword123!');
			await loginPage.expectLoginError();

			await context.close();
		});

		test('should navigate to register page via link', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();

			await page.goto('/auth/login');
			await page.locator('body[data-hydrated]').waitFor({ state: 'attached' });

			await page.getByRole('link', { name: 'Sign up' }).click();
			await expect(page).toHaveURL(/\/auth\/register/);

			await context.close();
		});
	});

	test.describe('Session Persistence', () => {
		test('should maintain session when navigating between pages', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);
			const storesPage = new StoresPage(page);

			// Login first
			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();
			await dashboardPage.waitForDashboardLoaded();

			// Navigate to stores
			await dashboardPage.navigateToStores();
			await storesPage.waitForStoresLoaded();
			await expect(storesPage.pageTitle).toBeVisible();

			// Navigate back to dashboard
			await page.getByRole('link', { name: 'Dashboard' }).click();
			await dashboardPage.waitForDashboardLoaded();
			await dashboardPage.expectLoggedInAs(TEST_USER.name);

			await context.close();
		});

		test('should maintain session after page refresh', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);

			// Login
			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();
			await dashboardPage.waitForDashboardLoaded();

			// Refresh the page. No networkidle here — signed-in pages hold the SSE
			// connection open, so the network never idles. waitForDashboardLoaded
			// below is the deterministic signal that the auth check completed.
			await page.reload();
			await dashboardPage.waitForDashboardLoaded();

			// Should still be logged in
			await dashboardPage.expectLoggedInAs(TEST_USER.name);
			await dashboardPage.expectNavVisible();

			await context.close();
		});

		test('should maintain session when using browser back/forward', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);

			// Login and navigate to stores
			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();
			await dashboardPage.waitForDashboardLoaded();
			await dashboardPage.navigateToStores();
			await expect(page).toHaveURL(/\/stores/);

			// Go back to dashboard
			await page.goBack();
			await dashboardPage.waitForDashboardLoaded();
			await dashboardPage.expectLoggedInAs(TEST_USER.name);

			// Go forward to stores
			await page.goForward();
			await expect(page).toHaveURL(/\/stores/);

			await context.close();
		});
	});

	test.describe('Logout', () => {
		test('should logout successfully and redirect to login', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);

			// Login first
			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();
			await dashboardPage.waitForDashboardLoaded();

			// Logout
			await dashboardPage.logout();

			// Should be redirected to login page
			await expect(page).toHaveURL(/\/auth\/login/);

			await context.close();
		});

		test('should not be able to access protected pages after logout', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);

			// Login
			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();
			await dashboardPage.waitForDashboardLoaded();

			// Logout
			await dashboardPage.logout();
			await expect(page).toHaveURL(/\/auth\/login/);

			// Try to access dashboard directly
			await page.goto('/dashboard');
			await page.waitForURL(/\/auth\/login/, { timeout: 10000 });

			// Try to access stores directly
			await page.goto('/stores');
			await page.waitForURL(/\/auth\/login/, { timeout: 10000 });

			await context.close();
		});

		test('should clear user state from navigation after logout', async ({ browser }) => {
			const context = await browser.newContext();
			const page = await context.newPage();
			const loginPage = new LoginPage(page);
			const dashboardPage = new DashboardPage(page);

			// Login
			await loginPage.goto();
			await loginPage.login(TEST_USER.email, TEST_USER.password);
			await loginPage.expectLoginSuccess();
			await dashboardPage.waitForDashboardLoaded();

			// Verify user name is shown
			await dashboardPage.expectLoggedInAs(TEST_USER.name);

			// Logout
			await dashboardPage.logout();
			await expect(page).toHaveURL(/\/auth\/login/);

			// Nav should not be visible on login page (no user state)
			await expect(page.locator('[data-user-menu]')).not.toBeVisible();

			await context.close();
		});
	});

	test.describe('Protected Routes', () => {
		test('should redirect unauthenticated users to login from dashboard', async ({ browser }) => {
			const context = await browser.newContext({ storageState: undefined });
			const page = await context.newPage();

			await page.goto('/dashboard');
			await page.waitForURL(/\/auth\/login/, { timeout: 10000 });

			await context.close();
		});

		test('should redirect unauthenticated users to login from stores', async ({ browser }) => {
			const context = await browser.newContext({ storageState: undefined });
			const page = await context.newPage();

			await page.goto('/stores');
			await page.waitForURL(/\/auth\/login/, { timeout: 10000 });

			await context.close();
		});

		test('should redirect unauthenticated users to login from product detail', async ({
			browser
		}) => {
			const context = await browser.newContext({ storageState: undefined });
			const page = await context.newPage();

			await page.goto('/products/some-product-id');
			await page.waitForURL(/\/auth\/login/, { timeout: 10000 });

			await context.close();
		});

		test('should allow access to public routes without authentication', async ({ browser }) => {
			const context = await browser.newContext({ storageState: undefined });
			const page = await context.newPage();

			// Home page should be accessible
			await page.goto('/');
			await expect(page).toHaveURL('/');

			// Login page should be accessible
			await page.goto('/auth/login');
			await expect(page).toHaveURL(/\/auth\/login/);

			// Register page should be accessible
			await page.goto('/auth/register');
			await expect(page).toHaveURL(/\/auth\/register/);

			await context.close();
		});
	});
});
