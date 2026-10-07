import { test, expect } from '@playwright/test';
import { RegisterPage } from '../pages/register.page';
import { DashboardPage } from '../pages/dashboard.page';

test.describe('Command Palette', () => {
	test('should open with Ctrl+K, search, navigate, and close with Escape', async ({
		browser
	}) => {
		test.setTimeout(30000);
		const context = await browser.newContext();
		const page = await context.newPage();

		const registerPage = new RegisterPage(page);
		const dashboardPage = new DashboardPage(page);

		const timestamp = Date.now();
		const testEmail = `cmdpalette-${timestamp}@example.com`;

		try {
			// Register and get to dashboard
			await registerPage.goto();
			await registerPage.register('Palette User', testEmail, 'PalettePassword123!');
			await registerPage.expectRegistrationSuccess();
			await dashboardPage.waitForDashboardLoaded();

			// Open command palette with Ctrl+K
			await page.keyboard.press('Control+k');
			const searchInput = page.getByPlaceholder('Search products, pages, tags...');
			await expect(searchInput).toBeVisible({ timeout: 3000 });
			await expect(searchInput).toBeFocused();

			// Should show navigation items
			await expect(page.getByRole('option', { name: /Dashboard/ })).toBeVisible();
			await expect(page.getByRole('option', { name: /Comparisons/ })).toBeVisible();

			// Close with Escape
			await page.keyboard.press('Escape');
			await expect(searchInput).not.toBeVisible();

			// Open again and navigate via search + Enter. Don't select by arrow-key
			// position — the palette's default list includes an Actions section, so
			// positional ArrowDown counts break whenever an entry is added or reordered.
			await page.keyboard.press('Control+k');
			await expect(searchInput).toBeVisible();

			await searchInput.fill('Comparisons');
			const comparisonsOption = page.getByRole('option', { name: /Comparisons/ }).first();
			// The query narrows the list until Comparisons is the highlighted result;
			// activate it with Enter (clicking races the list re-render on each keystroke).
			await expect(comparisonsOption).toHaveAttribute('aria-selected', 'true');
			await page.keyboard.press('Enter');

			await expect(page).toHaveURL(/\/comparisons/);
			await expect(searchInput).not.toBeVisible();
		} finally {
			await context.close();
		}
	});
});
