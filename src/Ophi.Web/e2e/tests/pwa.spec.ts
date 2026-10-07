import { test, expect } from '@playwright/test';

test.describe('PWA Support', () => {
	test('should serve manifest.json at /manifest.json', async ({ page }) => {
		const response = await page.goto('/manifest.json');
		expect(response).not.toBeNull();
		expect(response!.status()).toBe(200);

		const json = await response!.json();
		expect(json.name).toBe('Ophi - Price Tracker');
		expect(json.short_name).toBe('Ophi');
		expect(json.display).toBe('standalone');
		expect(json.theme_color).toBe('#0d9488');
		expect(json.icons).toHaveLength(2);
	});

	test('should have required meta tags in HTML', async ({ page }) => {
		await page.goto('/');

		const description = await page.locator('meta[name="description"]').getAttribute('content');
		expect(description).toContain('Ophi');

		const themeColor = await page.locator('meta[name="theme-color"]').getAttribute('content');
		expect(themeColor).toBe('#0d9488');

		const manifest = await page.locator('link[rel="manifest"]').getAttribute('href');
		expect(manifest).toBe('/manifest.json');

		const appleTouchIcon = await page.locator('link[rel="apple-touch-icon"]').getAttribute('href');
		expect(appleTouchIcon).toContain('icon-192x192');
	});

	test('should register a service worker', async ({ page }) => {
		await page.goto('/');

		// Wait for the service worker to register
		const swRegistered = await page.evaluate(async () => {
			if (!('serviceWorker' in navigator)) return false;
			const registration = await navigator.serviceWorker.getRegistration();
			return !!registration;
		});

		expect(swRegistered).toBe(true);
	});
});
