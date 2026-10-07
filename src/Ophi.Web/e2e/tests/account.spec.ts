import { test, expect } from '../fixtures/auth.fixture';
import type { Browser, BrowserContext, Page } from '@playwright/test';
import { AccountSettingsPage } from '../pages/account-settings.page';

const PASSWORD = 'E2eAccountPass123!';
const JSON_HEADERS = { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };

let userSeq = 0;
function uniqueEmail(tag: string): string {
	return `e2e-account-${Date.now()}-${userSeq++}-${Math.random().toString(36).slice(2, 6)}-${tag}@ophi.local`;
}

/**
 * Registers a throwaway user via the API. The auth cookie lands on the context's
 * shared cookie jar, so subsequent page navigations are authenticated without
 * driving the (rate-limited, dashboard-fanning-out) register/login UI. The
 * shared TEST_USER is never touched, so destructive ops here can't break the
 * rest of the suite.
 */
async function apiRegister(context: BrowserContext, email: string): Promise<void> {
	const resp = await context.request.post('/api/v1/auth/register', {
		data: { email, password: PASSWORD, name: 'E2E Account User' },
		headers: JSON_HEADERS
	});
	if (!resp.ok()) {
		throw new Error(`register failed: ${resp.status()} ${await resp.text()}`);
	}
}

/** Attempts a login in a throwaway context and returns the HTTP status. */
async function attemptLogin(browser: Browser, email: string, password: string): Promise<number> {
	const ctx = await browser.newContext({ storageState: undefined });
	const resp = await ctx.request.post('/api/v1/auth/login', {
		data: { email, password },
		headers: JSON_HEADERS
	});
	const status = resp.status();
	await ctx.close();
	return status;
}

interface FreshUser {
	context: BrowserContext;
	page: Page;
	account: AccountSettingsPage;
	email: string;
	password: string;
}

async function createFreshUser(browser: Browser, tag = ''): Promise<FreshUser> {
	const context = await browser.newContext({ storageState: undefined });
	const email = uniqueEmail(tag);
	await apiRegister(context, email);

	const page = await context.newPage();
	const account = new AccountSettingsPage(page);
	await account.goto();

	return { context, page, account, email, password: PASSWORD };
}

test.describe('Account Settings', () => {
	test.describe('Navigation', () => {
		// Read-only — safe to run as the shared TEST_USER.
		test('should mark the Account tab active under /settings', async ({ page }) => {
			const account = new AccountSettingsPage(page);
			await account.goto();

			await expect(account.settingsNav).toBeVisible();
			await expect(account.accountTab).toHaveAttribute('aria-current', 'page');
		});
	});

	test.describe('Profile', () => {
		test('should update the display name', async ({ browser }) => {
			const user = await createFreshUser(browser, 'name');
			try {
				await user.account.setName('Renamed Account User');
				await user.account.saveProfile();

				// Persists across a reload (server is the source of truth).
				await user.page.reload();
				await expect(user.account.profileName).toHaveValue('Renamed Account User');
			} finally {
				await user.context.close();
			}
		});

		test('should require current password to change email', async ({ browser }) => {
			const user = await createFreshUser(browser, 'email');
			try {
				// The current-password field only renders once the email is dirty.
				await expect(user.account.profileCurrentPassword).toBeHidden();

				const newEmail = uniqueEmail('changed');
				await user.account.setEmail(newEmail);

				await expect(user.account.profileCurrentPassword).toBeVisible();
				// Save stays disabled until the password is supplied.
				await expect(user.account.profileSave).toBeDisabled();

				await user.account.profileCurrentPassword.fill(user.password);
				await expect(user.account.profileSave).toBeEnabled();

				await user.account.saveProfile();

				await user.page.reload();
				await expect(user.account.profileEmail).toHaveValue(newEmail);
			} finally {
				await user.context.close();
			}
		});

		test('should reject an email change with the wrong password', async ({ browser }) => {
			const user = await createFreshUser(browser, 'wrongpw');
			try {
				await user.account.setEmail(uniqueEmail('wrong'));
				await user.account.profileCurrentPassword.fill('TotallyWrong123!');
				const response = await user.account.saveProfile();

				// Wrong current password → UnauthorizedException → 401.
				expect(response.status()).toBe(401);

				// The change is rejected — email is unchanged after a reload.
				await user.page.reload();
				await expect(user.account.profileEmail).toHaveValue(user.email);
			} finally {
				await user.context.close();
			}
		});
	});

	test.describe('Password', () => {
		test('should show a client-side error when new passwords do not match', async ({
			browser
		}) => {
			const user = await createFreshUser(browser, 'mismatch');
			try {
				await user.account.fillPasswordForm(user.password, 'NewPassw0rd!', 'Mismatch123!');
				await user.account.passwordSave.click();

				await expect(user.page.getByText('New passwords do not match')).toBeVisible();
			} finally {
				await user.context.close();
			}
		});

		test('should change the password and accept the new one on login', async ({ browser }) => {
			const user = await createFreshUser(browser, 'change');
			const newPassword = 'BrandNewPass456!';
			try {
				await user.account.fillPasswordForm(user.password, newPassword);
				await user.account.submitPasswordChange();

				expect(await attemptLogin(browser, user.email, newPassword)).toBe(200);
				expect(await attemptLogin(browser, user.email, user.password)).not.toBe(200);
			} finally {
				await user.context.close();
			}
		});

		test('should invalidate other sessions after a password change', async ({ browser }) => {
			const user = await createFreshUser(browser, 'invalidate');
			const newPassword = 'RotatedPass789!';

			// Second authenticated session for the same user (login via API).
			const otherCtx = await browser.newContext({ storageState: undefined });
			const otherLogin = await otherCtx.request.post('/api/v1/auth/login', {
				data: { email: user.email, password: user.password },
				headers: JSON_HEADERS
			});
			expect(otherLogin.ok()).toBeTruthy();

			try {
				// Sanity: the other session is currently authorized.
				await expect
					.poll(async () => (await otherCtx.request.get('/api/v1/products')).status())
					.toBe(200);

				// Rotate the password in the original session.
				await user.account.fillPasswordForm(user.password, newPassword);
				await user.account.submitPasswordChange();

				// The stale session's security stamp no longer matches → 401.
				await expect
					.poll(async () => (await otherCtx.request.get('/api/v1/products')).status())
					.toBe(401);
			} finally {
				await otherCtx.close();
				await user.context.close();
			}
		});
	});

	test.describe('Delete account', () => {
		test('should keep the account when the delete modal is cancelled', async ({ browser }) => {
			const user = await createFreshUser(browser, 'cancel');
			try {
				await user.account.openDeleteModal();
				await user.page.keyboard.press('Escape');
				await expect(user.account.deletePassword).toBeHidden();

				// Still authenticated.
				const resp = await user.context.request.get('/api/v1/products');
				expect(resp.ok()).toBeTruthy();
			} finally {
				await user.context.close();
			}
		});

		test('should delete the account and block re-login', async ({ browser }) => {
			const user = await createFreshUser(browser, 'delete');
			try {
				await user.account.openDeleteModal();
				await user.account.confirmDelete(user.password);

				// Redirected to login after deletion.
				await user.page.waitForURL(/\/auth\/login/, { timeout: 10000 });

				// The credentials no longer work.
				expect(await attemptLogin(browser, user.email, user.password)).not.toBe(200);
			} finally {
				await user.context.close();
			}
		});
	});
});
