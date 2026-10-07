import { test, expect } from '../fixtures/auth.fixture';
import { TestApi } from '../fixtures/api-helpers';
import { ScrapingSettingsPage } from '../pages/scraping-settings.page';

// These tests read-modify-restore the shared TEST_USER's single settings row. Under
// `fullyParallel` the file's tests would run in separate workers: each worker captures
// its own `original` snapshot (possibly mid-mutation from a sibling) and afterEach
// restores race the sibling's action→reload→assert window. Run the file sequentially.
test.describe.configure({ mode: 'default' });

test.describe('Scraping Settings', () => {
	let scraping: ScrapingSettingsPage;
	let api: TestApi;
	let original: Record<string, unknown> | undefined;

	test.beforeEach(async ({ page }) => {
		scraping = new ScrapingSettingsPage(page);
		api = new TestApi(page.request);
		original ??= await api.getSettings();
	});

	test.afterEach(async () => {
		if (!original) return;
		// Restore touched settings (0 = clear-to-null sentinel for nullable ints).
		await api.updateSettings({
			affiliatesEnabled: original.affiliatesEnabled,
			defaultCheckIntervalMinutes: original.defaultCheckIntervalMinutes ?? 0,
			pageFetchDelaySeconds: original.pageFetchDelaySeconds ?? 0,
			anomalyThresholdPercent: original.anomalyThresholdPercent ?? 0
		});
	});

	test('should toggle and persist affiliate links', async () => {
		await scraping.goto();
		const before = await scraping.affiliatesToggle.getAttribute('aria-checked');
		const after = before === 'true' ? 'false' : 'true';

		await scraping.toggleAffiliates();
		await expect(scraping.affiliatesToggle).toHaveAttribute('aria-checked', after);

		await scraping.page.reload();
		await expect(scraping.affiliatesToggle).toHaveAttribute('aria-checked', after);
	});

	test('should change and persist the default check interval', async () => {
		await scraping.goto();
		const current = await scraping.checkInterval.inputValue();
		const target = await scraping.checkInterval.evaluate(
			(el: HTMLSelectElement, cur: string) =>
				Array.from(el.options)
					.map((o) => o.value)
					.find((v) => v !== cur) ?? cur,
			current
		);

		await scraping.selectInterval(target);
		await expect(scraping.checkInterval).toHaveValue(target);

		await scraping.page.reload();
		await expect(scraping.checkInterval).toHaveValue(target);
	});

	test('should set and persist the page fetch delay', async () => {
		await scraping.goto();
		await scraping.setNumber(scraping.pageFetchDelay, 7);

		await scraping.page.reload();
		await expect(scraping.pageFetchDelay).toHaveValue('7');
	});

	test('should set and persist the anomaly threshold', async () => {
		await scraping.goto();
		await scraping.setNumber(scraping.anomalyThreshold, 25);

		await scraping.page.reload();
		await expect(scraping.anomalyThreshold).toHaveValue('25');
	});
});
