import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { DataSettingsPage } from '../pages/data-settings.page';
import fs from 'node:fs';

// The marker-matched cleanup in before/afterEach deletes every e2e-data product on
// the shared TEST_USER — under `fullyParallel` it would delete a parallel sibling's
// import mid-test. Run the file sequentially.
test.describe.configure({ mode: 'default' });

const MARKER = 'e2e-data';

async function cleanup(api: TestApi) {
	// pageSize=100 is the API max — the default (24) would leave strays beyond
	// page 1 uncleaned once the shared user accumulates products.
	const products = await api.get<{ items: Array<{ id: string; url: string }> }>(
		'/products?pageSize=100'
	);
	for (const p of products.items ?? []) {
		if (p.url?.includes(MARKER)) {
			await api.deleteProduct(p.id);
		}
	}
}

test.describe('Data Import / Export', () => {
	let dataPage: DataSettingsPage;
	let api: TestApi;

	test.beforeEach(async ({ page }) => {
		dataPage = new DataSettingsPage(page);
		api = new TestApi(page.request);
		await cleanup(api);
	});

	test.afterEach(async () => {
		await cleanup(api);
	});

	test('should export products as CSV', async () => {
		await dataPage.goto();
		const download = await dataPage.exportCsv();

		expect(download.suggestedFilename()).toBe('ophi-products.csv');
		const content = fs.readFileSync(await download.path(), 'utf-8');
		expect(content).toContain('url');
	});

	test('should export products as JSON', async () => {
		await dataPage.goto();
		const download = await dataPage.exportJson();

		expect(download.suggestedFilename()).toBe('ophi-products.json');
	});

	test('should import products from CSV', async () => {
		const url = `https://example.com/${MARKER}-${Date.now()}`;
		const csv = `url,name,target_price,tags\n${url},E2E Data Product,99.99,\n`;

		await dataPage.goto();
		const response = await dataPage.importCsv(csv);
		const result = (await response.json()) as { added: number; skipped: number };
		expect(result.added).toBe(1);

		// Durable check: the product now exists for the user.
		const products = await api.get<{ items: Array<{ url: string }> }>('/products');
		expect(products.items.some((p) => p.url === url)).toBeTruthy();
	});

	test('should round-trip an imported product back out through export', async () => {
		const url = `https://example.com/${MARKER}-rt-${Date.now()}`;
		const csv = `url,name,target_price,tags\n${url},E2E RoundTrip,49.99,\n`;

		await dataPage.goto();
		await dataPage.importCsv(csv);

		const download = await dataPage.exportCsv();
		const content = fs.readFileSync(await download.path(), 'utf-8');
		expect(content).toContain(url);
	});
});
