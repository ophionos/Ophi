import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ScrapingSettingsPage from '../../../routes/settings/scraping/+page.svelte';
import NotificationSettingsPage from '../../../routes/settings/notifications/+page.svelte';
import ApiKeysSettingsPage from '../../../routes/settings/api-keys/+page.svelte';
import { api } from '$lib/api/client';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/stores/toast.svelte', () => ({
	toast: {
		success: vi.fn(),
		error: vi.fn(),
		info: vi.fn()
	}
}));

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			updateSettings: vi.fn(),
			testDiscordWebhook: vi.fn(),
			sendTestEmail: vi.fn(),
			createWebhookTarget: vi.fn(),
			updateWebhookTarget: vi.fn(),
			deleteWebhookTarget: vi.fn(),
			testWebhookTarget: vi.fn(),
			createApiKey: vi.fn(),
			deleteApiKey: vi.fn()
		}
	};
});

const settings = {
	affiliatesEnabled: true,
	defaultCheckIntervalMinutes: null,
	pageFetchDelaySeconds: 5,
	scrapeCacheTtlMinutes: 0,
	discordWebhookConfigured: true,
	discordNotificationsEnabled: false,
	anomalyThresholdPercent: 50,
	autoPauseAfterFailures: 0,
	emailConfigured: true,
	emailNotificationsEnabled: true,
	telegramAvailable: false,
	telegramBotUsername: null,
	telegramConfigured: false,
	telegramNotificationsEnabled: false,
	pushoverAvailable: false,
	pushoverConfigured: false,
	pushoverNotificationsEnabled: false
};

beforeEach(() => {
	vi.clearAllMocks();
});

describe('Settings — Scraping page', () => {
	function renderPage() {
		return render(ScrapingSettingsPage, {
			props: { data: { settings } as never }
		});
	}

	it('should hydrate the controls from the loader settings', () => {
		renderPage();

		expect(screen.getByTestId('affiliates-toggle')).toHaveAttribute('aria-checked', 'true');
		expect(screen.getByTestId('page-fetch-delay')).toHaveValue(5);
		expect(screen.getByTestId('anomaly-threshold')).toHaveValue(50);
	});

	// The API stores 0 as NULL for these two, and the worker reads NULL as the GLOBAL default
	// (PriceAnomalyThreshold / MaxFailuresBeforeError) — not as "off". Saying "disabled" told
	// users protection was off while it was still running on the system default.
	it('should describe zero as resetting to the system default, not disabling', () => {
		renderPage();

		// Markup line-wraps inside the sentence, so compare on collapsed whitespace the way the
		// browser actually renders it.
		const collapse = (id: string) =>
			(screen.getByTestId(id).textContent ?? '').replace(/\s+/g, ' ').trim();
		const anomaly = collapse('anomaly-threshold-hint');
		const autoPause = collapse('auto-pause-failures-hint');

		expect(anomaly).toContain('system default');
		expect(anomaly).not.toContain('disabled');
		expect(autoPause).toContain('system default');
		expect(autoPause).not.toContain('disabled');
	});

	it('should not claim anomaly detection was disabled when zero is saved', async () => {
		vi.mocked(api.updateSettings).mockResolvedValue(settings as never);
		renderPage();

		await fireEvent.change(screen.getByTestId('anomaly-threshold'), { target: { value: '0' } });

		await waitFor(() => expect(api.updateSettings).toHaveBeenCalledWith({ anomalyThresholdPercent: 0 }));
		const message = vi.mocked(toast.success).mock.calls.at(-1)?.[0] ?? '';
		expect(message).not.toMatch(/disabled/i);
		expect(message).toMatch(/system default/i);
	});

	it('should not claim auto-pause was disabled when zero is saved', async () => {
		vi.mocked(api.updateSettings).mockResolvedValue(settings as never);
		renderPage();

		await fireEvent.change(screen.getByTestId('auto-pause-failures'), { target: { value: '0' } });

		await waitFor(() => expect(api.updateSettings).toHaveBeenCalledWith({ autoPauseAfterFailures: 0 }));
		const message = vi.mocked(toast.success).mock.calls.at(-1)?.[0] ?? '';
		expect(message).not.toMatch(/disabled/i);
		expect(message).toMatch(/system default/i);
	});

	// Cache TTL genuinely is off when unset — PriceCheckDispatcher only applies the floor when the
	// value is non-null — so its "0 = disabled" wording is correct and must not be swept up.
	it('should still describe scrape cache TTL as disabled at zero', () => {
		renderPage();

		expect(screen.getByTestId('scrape-cache-ttl-hint').textContent ?? '').toContain('disabled');
	});

	it('should persist a changed check interval', async () => {
		vi.mocked(api.updateSettings).mockResolvedValue(settings as never);
		renderPage();

		await fireEvent.change(screen.getByTestId('default-check-interval'), {
			target: { value: '120' }
		});

		await waitFor(() => {
			expect(api.updateSettings).toHaveBeenCalledWith({ defaultCheckIntervalMinutes: 120 });
		});
	});

	it('should toggle affiliates via the API', async () => {
		vi.mocked(api.updateSettings).mockResolvedValue(settings as never);
		renderPage();

		await fireEvent.click(screen.getByTestId('affiliates-toggle'));

		await waitFor(() => {
			expect(api.updateSettings).toHaveBeenCalledWith({ affiliatesEnabled: false });
		});
	});
});

describe('Settings — Notifications page', () => {
	function renderPage(overrides: Record<string, unknown> = {}) {
		return render(NotificationSettingsPage, {
			props: {
				data: { settings, webhooks: [], email: 'alerts@example.com', ...overrides } as never
			}
		});
	}

	// Email is the headline alert channel — the landing page promises it and alerts genuinely send
	// it — but the page used to have no email section at all, so the only way to discover a broken
	// SMTP setup was to never receive a price alert.
	it('should show the address alerts are sent to', () => {
		renderPage();

		expect(screen.getByTestId('email-address')).toHaveTextContent('alerts@example.com');
	});

	it('should report email as configured when the server has SMTP', () => {
		renderPage();

		const status = screen.getByTestId('email-status');
		expect(status).toHaveTextContent(/configured/i);
		expect(status).not.toHaveTextContent(/not configured/i);
		expect(screen.getByTestId('email-test-button')).toBeEnabled();
	});

	it('should warn and disable the test button when the server has no SMTP configured', () => {
		renderPage({ settings: { ...settings, emailConfigured: false } });

		expect(screen.getByTestId('email-status')).toHaveTextContent(/not configured/i);
		expect(screen.getByTestId('email-test-button')).toBeDisabled();
	});

	it('should send a test email via the API when the test button is clicked', async () => {
		vi.mocked(api.sendTestEmail).mockResolvedValue({
			success: true,
			sentTo: 'alerts@example.com'
		} as never);
		renderPage();

		await fireEvent.click(screen.getByTestId('email-test-button'));

		await waitFor(() => expect(api.sendTestEmail).toHaveBeenCalledWith());
		expect(vi.mocked(toast.success).mock.calls.at(-1)?.[0] ?? '').toContain('alerts@example.com');
	});

	it('should surface the server error when the test email fails', async () => {
		vi.mocked(api.sendTestEmail).mockResolvedValue({
			success: false,
			error: '535 authentication failed'
		} as never);
		renderPage();

		await fireEvent.click(screen.getByTestId('email-test-button'));

		await waitFor(() =>
			expect(vi.mocked(toast.error).mock.calls.at(-1)?.[0] ?? '').toContain(
				'535 authentication failed'
			)
		);
	});

	it('should show email notifications as on by default', () => {
		renderPage();

		expect(screen.getByTestId('email-toggle')).toHaveAttribute('aria-checked', 'true');
	});

	it('should turn email notifications off via the API', async () => {
		vi.mocked(api.updateSettings).mockResolvedValue({
			...settings,
			emailNotificationsEnabled: false
		} as never);
		renderPage();

		await fireEvent.click(screen.getByTestId('email-toggle'));

		await waitFor(() => {
			expect(api.updateSettings).toHaveBeenCalledWith({ emailNotificationsEnabled: false });
		});
		expect(screen.getByTestId('email-toggle')).toHaveAttribute('aria-checked', 'false');
	});

	it('should hydrate the toggle from the loader when email is off', () => {
		renderPage({ settings: { ...settings, emailNotificationsEnabled: false } });

		expect(screen.getByTestId('email-toggle')).toHaveAttribute('aria-checked', 'false');
		expect(screen.getByTestId('email-status')).toHaveTextContent(/turned off/i);
	});

	// The test button diagnoses SMTP; it is not an alert delivery, so opting out must not hide the
	// one control that proves the server can send at all.
	it('should keep the test button usable when email notifications are off', () => {
		renderPage({ settings: { ...settings, emailNotificationsEnabled: false } });

		expect(screen.getByTestId('email-test-button')).toBeEnabled();
	});

	it('should render the Discord block and webhook list', () => {
		renderPage();

		expect(screen.getByTestId('discord-toggle')).toBeInTheDocument();
		expect(screen.getByTestId('discord-toggle')).toHaveAttribute('aria-checked', 'false');
	});

	it('should enable the Test button only when a webhook is configured', () => {
		renderPage();

		// settings.discordWebhookConfigured is true in the fixture.
		expect(screen.getByTestId('discord-test-button')).toBeEnabled();
	});

	it('should hide the Telegram and Pushover cards when the server has no tokens', () => {
		renderPage();
		expect(screen.queryByTestId('telegram-card')).not.toBeInTheDocument();
		expect(screen.queryByTestId('pushover-card')).not.toBeInTheDocument();
	});

	it('should show available push channels and link the operator bot', () => {
		renderPage({
			settings: { ...settings, telegramAvailable: true, telegramBotUsername: 'OphiBot', pushoverAvailable: true }
		});
		expect(screen.getByTestId('telegram-card')).toBeInTheDocument();
		expect(screen.getByTestId('pushover-card')).toBeInTheDocument();
		expect(screen.getByRole('link', { name: '@OphiBot' })).toHaveAttribute('href', 'https://t.me/OphiBot');
	});

	it('should toggle Discord notifications via the API', async () => {
		vi.mocked(api.updateSettings).mockResolvedValue(settings as never);
		renderPage();

		await fireEvent.click(screen.getByTestId('discord-toggle'));

		await waitFor(() => {
			expect(api.updateSettings).toHaveBeenCalledWith({ discordNotificationsEnabled: true });
		});
	});
});

describe('Settings — API Keys page', () => {
	it('should render keys from the loader', () => {
		render(ApiKeysSettingsPage, {
			props: {
				data: {
					apiKeys: [
						{
							id: 'k1',
							name: 'CI key',
							scopes: ['read'],
							lastUsedAt: undefined,
							expiresAt: undefined,
							createdAt: new Date().toISOString()
						}
					]
				} as never
			}
		});

		expect(screen.getByText('CI key')).toBeInTheDocument();
	});
});
