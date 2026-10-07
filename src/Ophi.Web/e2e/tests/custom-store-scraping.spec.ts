import { test, expect } from '../fixtures/auth.fixture';
import { StoresPage } from '../pages/stores.page';
import { StoreModalPage } from '../pages/store-modal.page';
import { DashboardPage } from '../pages/dashboard.page';
import { createGlobaldataStoreData } from '../fixtures/test-data';

test.describe('Custom Store Scraping with Locale-Based Price Parsing', { tag: '@external' }, () => {
	// This test scrapes a real external URL (globaldata.pt) — flaky by design and not
	// suitable for the default e2e run. Use `bun run test:e2e:external` to include it.
	test.describe.configure({ retries: 2 });
	let storesPage: StoresPage;
	let storeModal: StoreModalPage;
	let dashboardPage: DashboardPage;
	let storeData: ReturnType<typeof createGlobaldataStoreData>;
	interface StoreItem { id: string; storeId: string }
	interface ProductItem { id: string; url: string; status: string; name: string; currentPrice: number; currency: string }

	let createdProductId: string | null = null;

	test.beforeEach(async ({ page }) => {
		storesPage = new StoresPage(page);
		storeModal = new StoreModalPage(page);
		dashboardPage = new DashboardPage(page);
		storeData = createGlobaldataStoreData();
		createdProductId = null;
	});

	test.afterEach(async ({ page }) => {
		// Clean up product via API
		if (createdProductId) {
			await page.request.delete(`/api/v1/products/${createdProductId}`).catch(() => {});
		}

		// Clean up store via API
		const storesResponse = await page.request.get('/api/v1/stores').catch(() => null);
		if (storesResponse?.ok()) {
			const stores = await storesResponse.json();
			const testStore = stores.items?.find(
				(s: StoreItem) => s.storeId === storeData.storeId
			);
			if (testStore?.id) {
				await page.request.delete(`/api/v1/stores/${testStore.id}`).catch(() => {});
			}
		}
	});

	test('should scrape product from custom store with European price format', async ({
		page
	}) => {
		// This test scrapes a real external URL — allow generous timeout
		test.setTimeout(120_000);

		const productUrl =
			'https://www.globaldata.pt/portatil-asus-vivobook-f1605va-16-i7-1355u-16gb-1tb-fhd+-iris-xe-f1605va-73blhdps1';

		// Clean up leftover test data from previous runs
		const existingProducts = await page.request.get('/api/v1/products');
		if (existingProducts.ok()) {
			const products = await existingProducts.json();
			const leftover = products.items?.find((p: ProductItem) => p.url === productUrl);
			if (leftover?.id) {
				await page.request.delete(`/api/v1/products/${leftover.id}`).catch(() => {});
			}
		}

		// Clean up leftover stores with globaldata.pt domain from previous runs
		const existingStores = await page.request.get('/api/v1/stores');
		if (existingStores.ok()) {
			const stores = await existingStores.json();
			const leftoverStores = stores.items?.filter(
				(s: StoreItem & { domainPatterns?: string[] }) =>
					s.storeId.startsWith('globaldata-pt-')
			) ?? [];
			for (const store of leftoverStores) {
				await page.request.delete(`/api/v1/stores/${store.id}`).catch(() => {});
			}
		}

		// Step 1: Create custom store configuration
		await storesPage.goto();
		await storesPage.waitForStoresLoaded();
		await storesPage.clickAddStore();
		await storeModal.expectOpen();
		await storeModal.expectCreateMode();

		await storeModal.fillForm(storeData);
		await storeModal.submit();
		await storeModal.expectClosed();

		// Verify store was created
		await storesPage.waitForStoresLoaded();
		const storesResponse = await page.request.get('/api/v1/stores');
		expect(storesResponse.ok()).toBeTruthy();
		const stores = await storesResponse.json();
		const createdStore = stores.items?.find(
			(s: StoreItem) => s.storeId === storeData.storeId
		);
		expect(createdStore).toBeTruthy();

		// Step 2: Add product from the custom store
		await dashboardPage.goto();
		await dashboardPage.waitForDashboardLoaded();

		const response = await dashboardPage.addProduct(productUrl);
		expect(response.status()).toBe(202);

		// Step 3: Poll the API directly for scraping completion (avoids networkidle issues)
		let productData: ProductItem | null = null;
		for (let i = 0; i < 20; i++) {
			await page.waitForTimeout(3000);

			const productsResponse = await page.request.get('/api/v1/products');
			if (productsResponse.ok()) {
				const products = await productsResponse.json();
				const product = products.items?.find((p: ProductItem) => p.url === productUrl);
				if (product && product.status === 'active') {
					productData = product;
					break;
				}
				// If product errored out, fail fast with the error details
				if (product && product.status === 'error') {
					// Fetch detail endpoint for more info
					const detailResp = await page.request.get(
						`/api/v1/products/${product.id}`
					);
					const detail = detailResp.ok() ? await detailResp.json() : null;
					throw new Error(
						`Scraping failed. Product: ${JSON.stringify(detail ?? product, null, 2)}`
					);
				}
			}
		}

		// Step 4: Assert product was scraped correctly
		expect(productData, 'Product should have been scraped and become Active').not.toBeNull();
		expect(productData.status).toBe('active');

		createdProductId = productData.id;

		// Product name should contain ASUS or Vivobook or Portatil
		expect(
			productData.name.includes('ASUS') ||
				productData.name.includes('VivoBook') ||
				productData.name.includes('Portátil') ||
				productData.name.includes('Portatil')
		).toBeTruthy();

		// Price should be correctly parsed (not 64900 from wrong locale)
		expect(productData.currentPrice).toBeGreaterThan(100);
		expect(productData.currentPrice).toBeLessThan(5000);
		expect(productData.currentPrice).not.toBe(64900);

		// Currency should be EUR (from the meta tag)
		expect(productData.currency).toBe('EUR');
	});
});
