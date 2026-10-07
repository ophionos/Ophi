import { Page, Locator, expect } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Page Object for /settings/account.
 *
 * SAFETY: profile/password/delete operations mutate (or destroy) the logged-in
 * user. Account specs must drive this POM with a freshly-registered throwaway
 * user in an isolated context — never the shared TEST_USER, whose session every
 * other spec depends on.
 */
export class AccountSettingsPage extends BasePage {
	readonly settingsNav: Locator;
	readonly accountTab: Locator;

	// Profile card
	readonly profileName: Locator;
	readonly profileEmail: Locator;
	readonly profileCurrentPassword: Locator;
	readonly profileSave: Locator;

	// Password card
	readonly passwordCurrent: Locator;
	readonly passwordNew: Locator;
	readonly passwordConfirm: Locator;
	readonly passwordSave: Locator;

	// Danger zone + delete modal
	readonly deleteOpen: Locator;
	readonly deletePassword: Locator;
	readonly deleteConfirm: Locator;

	constructor(page: Page) {
		super(page);
		this.settingsNav = page.getByTestId('settings-nav');
		this.accountTab = this.settingsNav.getByRole('link', { name: 'Account' });

		this.profileName = page.getByTestId('profile-name');
		this.profileEmail = page.getByTestId('profile-email');
		this.profileCurrentPassword = page.getByTestId('profile-current-password');
		this.profileSave = page.getByTestId('profile-save');

		this.passwordCurrent = page.getByTestId('password-current');
		this.passwordNew = page.getByTestId('password-new');
		this.passwordConfirm = page.getByTestId('password-confirm');
		this.passwordSave = page.getByTestId('password-save');

		this.deleteOpen = page.getByTestId('delete-account-open');
		this.deletePassword = page.getByTestId('delete-account-password');
		this.deleteConfirm = page.getByTestId('delete-account-confirm');
	}

	async goto() {
		// Authenticated pages hold an open SSE (live-updates) connection, so
		// `networkidle` never settles — wait for a concrete element instead.
		await this.page.goto('/settings/account', { waitUntil: 'domcontentloaded' });
		await expect(this.profileSave).toBeVisible();
		// A fill before hydration can hold in the DOM while the bound $state never sees it, so
		// Save stays disabled — the chronic retry-flake on the Profile specs.
		await this.waitForPageLoad();
	}

	/**
	 * Fills a field and verifies the value stuck, retrying if it didn't.
	 * The profile card runs an `$effect` that re-syncs name/email from loader
	 * data on mount; a fill that lands before hydration completes gets clobbered
	 * by that effect, so we retry until it holds.
	 */
	private async fillStable(locator: Locator, value: string) {
		await expect(async () => {
			await locator.fill(value);
			// Let any hydration-time $effect clobber fire before verifying, so a
			// passing iteration means the value held (no pending reset).
			await this.page.waitForTimeout(300);
			await expect(locator).toHaveValue(value, { timeout: 500 });
		}).toPass({ timeout: 15000 });
	}

	// --- Profile ---
	async setName(name: string) {
		await this.fillStable(this.profileName, name);
	}

	async setEmail(email: string) {
		await this.fillStable(this.profileEmail, email);
	}

	async saveProfile() {
		const responsePromise = this.page.waitForResponse(
			(resp) =>
				resp.url().includes('/api/v1/account/profile') && resp.request().method() === 'PUT'
		);
		await this.profileSave.click();
		return responsePromise;
	}

	// --- Password ---
	async fillPasswordForm(current: string, next: string, confirm = next) {
		await this.passwordCurrent.fill(current);
		await this.passwordNew.fill(next);
		await this.passwordConfirm.fill(confirm);
	}

	async submitPasswordChange() {
		const responsePromise = this.page.waitForResponse(
			(resp) =>
				resp.url().includes('/api/v1/account/password') && resp.request().method() === 'PUT'
		);
		await this.passwordSave.click();
		return responsePromise;
	}

	// --- Delete ---
	async openDeleteModal() {
		// Retry the click until the modal opens — a click that lands before the
		// handler is hydrated is a no-op.
		await expect(async () => {
			await this.deleteOpen.click();
			await expect(this.deletePassword).toBeVisible({ timeout: 1000 });
		}).toPass({ timeout: 15000 });
	}

	async confirmDelete(password: string) {
		await this.deletePassword.fill(password);
		const responsePromise = this.page.waitForResponse(
			(resp) => resp.url().includes('/api/v1/account') && resp.request().method() === 'DELETE'
		);
		await this.deleteConfirm.click();
		return responsePromise;
	}
}
