import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';

// The shared TEST_USER owns the shipping rates and the landed-prices setting; run sequentially.
test.describe.configure({ mode: 'default' });

test.describe('Shipping costs and landed prices', () => {
	// SKIPPED: the comparison best price only changes when two products have scraped prices on
	// different hosts. The suite's API helpers can only create name-only products
	// (POST /products/create), with no URL and no price, and nothing in the suite can seed
	// PricePoints or ProductUrl.CurrentPrice without scraping a live/mock store. Un-skip once a
	// helper can seed priced URLs; the flow below is the intended end-to-end check.
	test.skip('should change the comparison best product when a shipping cost is set and the setting is on', async ({
		page
	}) => {
		const api = new TestApi(page.request);
		const stamp = Date.now();
		const cheapHost = `cheap-${stamp}.example`;
		const dearHost = `dear-${stamp}.example`;

		// Seed (needs the missing helper): "Cheap" costs 18.99 on cheapHost, "Dear" 19.99 on dearHost.
		const group = await api.createComparison(`E2E Shipping ${stamp}`);
		const cheap = await api.createProduct(`E2E Cheap ${stamp}`);
		const dear = await api.createProduct(`E2E Dear ${stamp}`);
		await api.addProductToGroup(group.id, cheap.id);
		await api.addProductToGroup(group.id, dear.id);

		try {
			// A 3.00 EUR rate on the cheap host makes the dear one the landed best.
			await page.goto('/stores');
			await page.getByRole('button', { name: 'Add shipping cost' }).click();
			await page.getByLabel('Site host').fill(cheapHost);
			await page.getByLabel('Shipping cost').fill('3');
			await page.getByLabel('Currency').fill('EUR');
			await page.getByRole('button', { name: 'Save' }).click();
			await expect(page.getByTestId('shipping-rate-row')).toContainText(cheapHost);

			// Setting off: the scraped cheapest still wins.
			await page.goto(`/comparisons/${group.id}`);
			await expect(page.getByTestId('cheapest-spotlight')).toContainText(`E2E Cheap ${stamp}`);

			// Setting on: the landed best (Dear: 19.99 < 21.99) wins, labelled incl. shipping.
			await page.goto('/settings/account');
			await page.getByRole('switch', { name: 'Show prices with shipping' }).click();
			await expect(
				page.getByRole('switch', { name: 'Show prices with shipping' })
			).toHaveAttribute('aria-checked', 'true');

			await page.goto(`/comparisons/${group.id}`);
			const spotlight = page.getByTestId('cheapest-spotlight');
			await expect(spotlight).toContainText(`E2E Dear ${stamp}`);
			await expect(spotlight).toContainText('incl. shipping');
		} finally {
			await api.updateSettings({ showLandedPrices: false });
			await api.delete(`/shipping-rates/${encodeURIComponent(cheapHost)}`);
			await api.delete(`/shipping-rates/${encodeURIComponent(dearHost)}`);
			await api.deleteComparison(group.id);
			await api.deleteProduct(cheap.id);
			await api.deleteProduct(dear.id);
		}
	});
});
