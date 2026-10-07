import { Page, Locator } from '@playwright/test';

export class BasePage {
	readonly page: Page;

	constructor(page: Page) {
		this.page = page;
	}

	async waitForPageLoad() {
		// Wait for hydration, not `networkidle`: the app-wide SSE connection
		// (/api/v1/events) keeps the network permanently busy on every signed-in
		// page, so networkidle never settles and times out. The root layout sets
		// body[data-hydrated] in onMount, which fires when hydration completes.
		await this.page.locator('body[data-hydrated]').waitFor({ state: 'attached' });
	}

	async waitForApiResponse(urlPattern: string | RegExp) {
		return this.page.waitForResponse(urlPattern);
	}

	getByTestId(testId: string): Locator {
		return this.page.getByTestId(testId);
	}
}
