import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
	testDir: './e2e/tests',
	fullyParallel: true,
	forbidOnly: !!process.env.CI,
	retries: process.env.CI ? 2 : 1,
	workers: process.env.CI ? 1 : undefined,
	reporter: [['html', { outputFolder: 'playwright-report' }], ['list']],
	timeout: 30000,
	expect: {
		timeout: 10000
	},

	use: {
		baseURL: process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:3000',
		trace: 'on-first-retry',
		screenshot: 'only-on-failure',
		video: 'retain-on-failure',
		actionTimeout: 10000,
		navigationTimeout: 15000
	},

	projects: [
		// Setup project for creating authenticated state
		{
			name: 'setup',
			testMatch: /global-setup\.ts/,
			testDir: './e2e'
		},
		{
			name: 'chromium',
			use: {
				...devices['Desktop Chrome'],
				storageState: 'e2e/.auth/user.json'
			},
			dependencies: ['setup']
		}
	],

	// Web server configuration - starts dev server before tests
	webServer: {
		command: 'bun run dev',
		url: 'http://localhost:3000',
		reuseExistingServer: !process.env.CI,
		timeout: 120 * 1000,
		env: {
			API_URL: 'http://localhost:5041'
		}
	}
});
