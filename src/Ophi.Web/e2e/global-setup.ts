import { test as setup, expect } from '@playwright/test';
import { TEST_USER } from './fixtures/auth.fixture';

setup('authenticate', async ({ page }) => {
	// Guard: the suite hard-requires the vite dev server proxying to a Development
	// API (effectively-unlimited auth rate limit, no scrape worker). With
	// `reuseExistingServer`, a compose stack already listening on :3000 (production
	// config) gets silently reused instead, and the suite fails confusingly — auth
	// 429s in account.spec, a non-empty scrape-health dashboard, real rate limits.
	// The vite dev server serves its HMR client at /@vite/client; the compose web
	// image (adapter-node) 404s it.
	const viteProbe = await page.request.get('/@vite/client');
	if (!viteProbe.ok()) {
		throw new Error(
			`The e2e target does not look like the vite dev server (GET /@vite/client → ${viteProbe.status()}). ` +
				'You are probably hitting the WSL2 compose stack (production config) on :3000. ' +
				'Stop its web container or start the native dev stack (see .claude/skills/dev-stack) ' +
				'and point PLAYWRIGHT_BASE_URL at the vite server.'
		);
	}

	// Register the shared TEST_USER via the API request context — the session cookie
	// lands on the browser context's shared jar (same pattern as account.spec's
	// apiRegister). Do NOT drive the register form here: on a cold vite dev server
	// (every CI run) hydration can lose the race with the fills, so the click fires
	// the native GET form submit and no POST ever reaches the API — this exact
	// failure took down the first nightly e2e run. The register/login UI has its
	// own specs; global setup is infrastructure.
	const JSON_HEADERS = { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
	const register = await page.request.post('/api/v1/auth/register', {
		data: { email: TEST_USER.email, password: TEST_USER.password, name: TEST_USER.name },
		headers: JSON_HEADERS
	});
	if (!register.ok()) {
		// Most likely the user already exists from a previous run — sign in instead.
		const login = await page.request.post('/api/v1/auth/login', {
			data: { email: TEST_USER.email, password: TEST_USER.password },
			headers: JSON_HEADERS
		});
		if (!login.ok()) {
			throw new Error(
				`Could not authenticate TEST_USER: register → ${register.status()}, ` +
					`login → ${login.status()} ${await login.text()}`
			);
		}
	}

	// Verify the session works in a real browser navigation. Generous timeout: on a
	// cold vite dev server (every CI run) the dashboard route compiles on first hit
	// and its data load can outlast the default 10s expect.
	await page.goto('/dashboard');
	await expect(page.getByRole('heading', { name: 'Your Products' })).toBeVisible({
		timeout: 20_000
	});

	// Clean up leftover test stores from previous runs
	const storesResponse = await page.request.get('/api/v1/stores');
	if (storesResponse.ok()) {
		const stores = await storesResponse.json();
		const testStores = stores.items?.filter(
			(s: { storeId: string }) => s.storeId.startsWith('test-store-') || s.storeId.startsWith('globaldata-pt-')
		) ?? [];
		for (const store of testStores) {
			await page.request.delete(`/api/v1/stores/${(store as { id: string }).id}`).catch(() => {});
		}
	}

	// Clean up leftover test products from previous runs. pageSize=100 is the API
	// max — the default (24) would leave strays beyond page 1 uncleaned.
	const productsResponse = await page.request.get('/api/v1/products?pageSize=100');
	if (productsResponse.ok()) {
		const products = await productsResponse.json();
		const testProducts = products.items?.filter(
			(p: { url: string }) =>
				p.url.includes('example.com') || p.url.includes('journey-product')
		) ?? [];
		for (const product of testProducts) {
			await page.request.delete(`/api/v1/products/${(product as { id: string }).id}`).catch(() => {});
		}
	}

	// Save authentication state
	await page.context().storageState({ path: 'e2e/.auth/user.json' });
});
