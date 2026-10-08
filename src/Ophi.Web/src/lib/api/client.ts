const VITE_API_URL = import.meta.env.VITE_API_URL;
if (!VITE_API_URL) throw new Error('VITE_API_URL is not configured. Set VITE_API_URL in your environment.');
export const API_BASE = VITE_API_URL;

export class ApiError extends Error {
	public readonly code: string;
	public readonly details: Record<string, string[]>;

	constructor(code: string, message: string, details?: Record<string, string[]>) {
		super(message);
		this.name = 'ApiError';
		this.code = code;
		// Normalize field names to lowercase for consistent frontend access
		this.details = ApiError.normalizeDetails(details);
	}

	private static normalizeDetails(details?: Record<string, string[]>): Record<string, string[]> {
		if (!details) return {};
		const normalized: Record<string, string[]> = {};
		for (const [key, value] of Object.entries(details)) {
			normalized[key.toLowerCase()] = value;
		}
		return normalized;
	}

	getFieldErrors(field: string): string[] {
		return this.details[field.toLowerCase()] ?? [];
	}

	hasFieldErrors(): boolean {
		return Object.keys(this.details).length > 0;
	}
}

class ApiClient {
	private fetchImpl: typeof fetch | null = null;

	withFetch(fetcher: typeof fetch): ApiClient {
		const scoped = new ApiClient();
		scoped.fetchImpl = fetcher;
		return scoped;
	}

	private async request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
		const fetcher = this.fetchImpl ?? fetch;
		const response = await fetcher(`${API_BASE}${endpoint}`, {
			...options,
			credentials: 'include',
			cache: 'no-store',
			headers: {
				'Content-Type': 'application/json',
				'X-Requested-With': 'XMLHttpRequest',
				...options.headers
			}
		});

		if (!response.ok) {
			let error: { error?: string; message?: string; details?: Record<string, string[]> } = {};
			try {
				error = await response.json();
			} catch {
				// Non-JSON response body (e.g. 502 HTML gateway error)
			}
			throw new ApiError(
				error.error || 'UnknownError',
				error.message || 'An error occurred',
				error.details
			);
		}

		if (response.status === 204) {
			return {} as T;
		}

		try {
			return (await response.json()) as T;
		} catch {
			// Some success responses have an empty body with no JSON to parse —
			// e.g. 202 Accepted from async endpoints like retry-scrape, which
			// return Results.Accepted() with no content. Treat as an empty result.
			return {} as T;
		}
	}

	// Auth
	async register(email: string, password: string, name: string) {
		return this.request<{ id: string; email: string; name: string }>('/auth/register', {
			method: 'POST',
			body: JSON.stringify({ email, password, name })
		});
	}

	async getRegistrationStatus() {
		return this.request<{ open: boolean }>('/auth/registration');
	}

	async login(email: string, password: string) {
		return this.request<{ id: string; email: string; name: string }>('/auth/login', {
			method: 'POST',
			body: JSON.stringify({ email, password })
		});
	}

	async logout() {
		return this.request('/auth/logout', { method: 'POST' });
	}

	async me() {
		return this.request<{ id: string; email: string; name: string }>('/auth/me');
	}

	async forgotPassword(email: string) {
		return this.request<{ message: string }>('/auth/forgot-password', {
			method: 'POST',
			body: JSON.stringify({ email })
		});
	}

	async resetPassword(token: string, newPassword: string) {
		return this.request<{ message: string }>('/auth/reset-password', {
			method: 'POST',
			body: JSON.stringify({ token, newPassword })
		});
	}

	// Account (signed-in self-service)
	async changePassword(currentPassword: string, newPassword: string) {
		return this.request<{ message: string }>('/account/password', {
			method: 'PUT',
			body: JSON.stringify({ currentPassword, newPassword })
		});
	}

	// `pendingEmail` is set when the server mailed a confirmation link instead of changing the email.
	async updateProfile(data: { name: string; email: string; currentPassword?: string }) {
		return this.request<{ id: string; email: string; name: string; pendingEmail?: string | null }>(
			'/account/profile',
			{
				method: 'PUT',
				body: JSON.stringify(data)
			}
		);
	}

	async confirmEmailChange(token: string) {
		return this.request<{ email: string }>('/account/email/confirm', {
			method: 'POST',
			body: JSON.stringify({ token })
		});
	}

	async deleteAccount(password: string) {
		return this.request<void>('/account', {
			method: 'DELETE',
			body: JSON.stringify({ password })
		});
	}

	// Products
	async getProducts(params?: GetProductsParams) {
		const query = new URLSearchParams();
		if (params?.tagId) query.set('tagId', params.tagId);
		if (params?.search) query.set('search', params.search);
		if (params?.status) query.set('status', params.status);
		if (params?.sortBy) query.set('sortBy', params.sortBy);
		if (params?.sortDirection) query.set('sortDirection', params.sortDirection);
		if (params?.page) query.set('page', params.page.toString());
		if (params?.pageSize) query.set('pageSize', params.pageSize.toString());
		if (params?.includeSparkline) query.set('includeSparkline', 'true');
		if (params?.atLowest) query.set('atLowest', 'true');
		if (params?.favourite) query.set('favourite', 'true');
		if (params?.priceDrop) query.set('priceDrop', 'true');
		if (params?.hasAlerts) query.set('hasAlerts', 'true');
		const qs = query.toString();
		return this.request<{
			items: Product[];
			total: number;
			page: number;
			pageSize: number;
			atLowestCount: number;
			priceDropCount: number;
			withAlertsCount: number;
		}>(`/products${qs ? `?${qs}` : ''}`);
	}

	async addProduct(url: string) {
		return this.request<Product>('/products', {
			method: 'POST',
			body: JSON.stringify({ url })
		});
	}

	async createProduct(data: CreateProductRequest) {
		return this.request<Product>('/products/create', {
			method: 'POST',
			body: JSON.stringify(data)
		});
	}

	async updateProduct(id: string, data: UpdateProductRequest) {
		return this.request<Product>(`/products/${id}`, {
			method: 'PUT',
			body: JSON.stringify(data)
		});
	}

	async deleteProduct(id: string) {
		return this.request(`/products/${id}`, { method: 'DELETE' });
	}

	async getPriceHistory(productId: string, days = 30) {
		return this.request<PriceHistory>(`/products/${productId}/history?days=${days}`);
	}

	async getProduct(id: string) {
		return this.request<ProductDetail>(`/products/${id}`);
	}

	async addProductUrl(productId: string, url: string) {
		return this.request<ProductUrl>(`/products/${productId}/urls`, {
			method: 'POST',
			body: JSON.stringify({ url })
		});
	}

	async removeProductUrl(productId: string, urlId: string) {
		return this.request(`/products/${productId}/urls/${urlId}`, { method: 'DELETE' });
	}

	async getScrapeLog(productId: string, limit = 20) {
		return this.request<{ items: ScrapeLogEntry[] }>(`/products/${productId}/scrape-log?limit=${limit}`);
	}

	async retryScrapeProductUrl(productId: string, urlId: string) {
		return this.request(`/products/${productId}/urls/${urlId}/retry`, { method: 'POST' });
	}

	async resumeProductUrl(productId: string, urlId: string) {
		return this.request(`/products/${productId}/urls/${urlId}/resume`, { method: 'POST' });
	}

	// Tags
	async getTags(search?: string) {
		const query = new URLSearchParams();
		if (search) query.set('search', search);
		const qs = query.toString();
		return this.request<{ items: Tag[] }>(`/tags${qs ? `?${qs}` : ''}`);
	}

	async createTag(name: string, color?: string, weight?: number) {
		return this.request<Tag>('/tags', {
			method: 'POST',
			body: JSON.stringify({ name, color, weight })
		});
	}

	async updateTag(id: string, data: UpdateTagRequest) {
		return this.request<Tag>(`/tags/${id}`, {
			method: 'PUT',
			body: JSON.stringify(data)
		});
	}

	async deleteTag(id: string) {
		return this.request(`/tags/${id}`, { method: 'DELETE' });
	}

	async addTagToProduct(productId: string, tagId: string) {
		return this.request(`/products/${productId}/tags`, {
			method: 'POST',
			body: JSON.stringify({ tagId })
		});
	}

	async removeTagFromProduct(productId: string, tagId: string) {
		return this.request(`/products/${productId}/tags/${tagId}`, {
			method: 'DELETE'
		});
	}

	// Alerts
	async getAlerts() {
		return this.request<{ items: Alert[] }>('/alerts');
	}

	async createAlert(productId: string, targetPrice: number, condition: AlertCondition) {
		return this.request<Alert>('/alerts', {
			method: 'POST',
			body: JSON.stringify({ productId, targetPrice, condition })
		});
	}

	/**
	 * Moves a dormant alert onto its product's current currency with a new target.
	 *
	 * `expectedCurrency` is the currency the user was shown when they picked `targetPrice`. The
	 * server rejects the call if the product has re-anchored again since, rather than stamping the
	 * number with a denomination the user never saw.
	 */
	async redenominateAlert(id: string, targetPrice: number, expectedCurrency: string) {
		return this.request<Alert>(`/alerts/${id}/redenominate`, {
			method: 'POST',
			body: JSON.stringify({ targetPrice, expectedCurrency })
		});
	}

	/** Pauses (`false`) or resumes (`true`) an alert. Resuming can 422 `MaxAlertsReached`. */
	async setAlertActive(id: string, active: boolean) {
		return this.request<Alert>(`/alerts/${id}`, {
			method: 'PATCH',
			body: JSON.stringify({ active })
		});
	}

	async deleteAlert(id: string) {
		return this.request(`/alerts/${id}`, { method: 'DELETE' });
	}

	// Comparisons
	async getComparisonGroups() {
		return this.request<{ items: ComparisonGroup[] }>('/comparisons');
	}

	async getComparisonGroup(id: string, days = 30) {
		return this.request<ComparisonGroupDetail>(`/comparisons/${id}?days=${days}`);
	}

	async createComparisonGroup(name: string, description?: string) {
		return this.request<ComparisonGroup>('/comparisons', {
			method: 'POST',
			body: JSON.stringify({ name, description })
		});
	}

	async deleteComparisonGroup(id: string) {
		return this.request(`/comparisons/${id}`, { method: 'DELETE' });
	}

	async addProductToComparisonGroup(groupId: string, productId: string) {
		return this.request(`/comparisons/${groupId}/products`, {
			method: 'POST',
			body: JSON.stringify({ productId })
		});
	}

	async addProductsToComparisonGroup(groupId: string, productIds: string[]) {
		return this.request(`/comparisons/${groupId}/products/batch`, {
			method: 'POST',
			body: JSON.stringify({ productIds })
		});
	}

	async removeProductFromComparisonGroup(groupId: string, productId: string) {
		return this.request(`/comparisons/${groupId}/products/${productId}`, {
			method: 'DELETE'
		});
	}

	// Stores
	async getStores(search?: string) {
		const query = new URLSearchParams();
		if (search) query.set('search', search);
		const qs = query.toString();
		return this.request<{ items: Store[]; total: number }>(`/stores${qs ? `?${qs}` : ''}`);
	}

	async createStore(data: CreateStoreRequest) {
		return this.request<Store>('/stores', {
			method: 'POST',
			body: JSON.stringify(data)
		});
	}

	async updateStore(id: string, data: UpdateStoreRequest) {
		return this.request<Store>(`/stores/${id}`, {
			method: 'PUT',
			body: JSON.stringify(data)
		});
	}

	async deleteStore(id: string) {
		return this.request(`/stores/${id}`, { method: 'DELETE' });
	}

	async testStore(storeId: string, testUrl: string) {
		return this.request<TestStoreResult>('/stores/test', {
			method: 'POST',
			body: JSON.stringify({ storeId, testUrl })
		});
	}

	async exportStore(id: string) {
		return this.request<StoreExport>(`/stores/${id}/export`);
	}

	async detectStore(url: string) {
		return this.request<DetectStoreResult>('/stores/detect', {
			method: 'POST',
			body: JSON.stringify({ url })
		});
	}

	async importStore(data: StoreImportRequest) {
		return this.request<{ id: string; storeId: string; name: string; createdAt: string }>(
			'/stores/import',
			{
				method: 'POST',
				body: JSON.stringify(data)
			}
		);
	}

	// Settings
	async getSettings() {
		return this.request<Settings>('/settings');
	}

	async updateSettings(data: UpdateSettingsRequest) {
		return this.request<Settings>('/settings', {
			method: 'PUT',
			body: JSON.stringify(data)
		});
	}

	async testDiscordWebhook() {
		return this.request<TestDiscordWebhookResponse>('/settings/discord/test', {
			method: 'POST'
		});
	}

	/** ECB euro reference rates for display-currency conversion (display only). */
	async getFxRates() {
		return this.request<FxRates>('/fx-rates');
	}

	/** Sends a sample alert to the saved Telegram chat / Pushover user key / ntfy topic. */
	async testPushChannel(channel: PushChannel) {
		return this.request<TestDiscordWebhookResponse>(`/settings/${channel}/test`, {
			method: 'POST'
		});
	}

	/** Sends a test email. Takes no recipient — the server uses the signed-in user's own address. */
	async sendTestEmail() {
		return this.request<SendTestEmailResponse>('/settings/email/test', {
			method: 'POST'
		});
	}

	// Scrape Health
	async getScrapeHealth() {
		return this.request<ScrapeHealthResponse>('/scrape-health');
	}

	async setStoreAffiliate(storeId: string, data: SetStoreAffiliateRequest) {
		return this.request<{ storeId: string; affiliateParamName?: string; affiliateTag?: string }>(
			`/stores/${storeId}/affiliate`,
			{
				method: 'PUT',
				body: JSON.stringify(data)
			}
		);
	}

	// Notifications
	async getNotifications(params?: { unreadOnly?: boolean; page?: number; pageSize?: number }) {
		const query = new URLSearchParams();
		if (params?.unreadOnly) query.set('unreadOnly', 'true');
		if (params?.page) query.set('page', params.page.toString());
		if (params?.pageSize) query.set('pageSize', params.pageSize.toString());
		const qs = query.toString();
		return this.request<{ items: Notification[]; total: number; page: number; pageSize: number }>(`/notifications${qs ? `?${qs}` : ''}`);
	}

	async getNotificationCount() {
		return this.request<{ unread: number }>('/notifications/count');
	}

	async markNotificationRead(id: string) {
		return this.request(`/notifications/${id}/read`, { method: 'PUT' });
	}

	async markAllNotificationsRead() {
		return this.request('/notifications/read-all', { method: 'POST' });
	}

	// Webhooks
	async getWebhookTargets() {
		return this.request<WebhookTarget[]>('/webhooks');
	}

	async createWebhookTarget(data: CreateWebhookTargetRequest) {
		return this.request<WebhookTarget>('/webhooks', {
			method: 'POST',
			body: JSON.stringify(data)
		});
	}

	async updateWebhookTarget(id: string, data: UpdateWebhookTargetRequest) {
		return this.request<WebhookTarget>(`/webhooks/${id}`, {
			method: 'PUT',
			body: JSON.stringify(data)
		});
	}

	async deleteWebhookTarget(id: string) {
		return this.request(`/webhooks/${id}`, { method: 'DELETE' });
	}

	async testWebhookTarget(id: string) {
		return this.request<TestWebhookTargetResponse>(`/webhooks/${id}/test`, { method: 'POST' });
	}

	// API Keys
	async listApiKeys() {
		return this.request<ApiKeyInfo[]>('/api-keys');
	}

	async createApiKey(data: CreateApiKeyRequest) {
		return this.request<ApiKeyCreatedResponse>('/api-keys', {
			method: 'POST',
			body: JSON.stringify(data)
		});
	}

	async deleteApiKey(id: string) {
		return this.request(`/api-keys/${id}`, { method: 'DELETE' });
	}

	// Export/Import
	async exportProducts(format: 'csv' | 'json' = 'csv') {
		const response = await fetch(`${API_BASE}/products/export?format=${format}`, {
			credentials: 'include',
			cache: 'no-store',
			headers: { 'X-Requested-With': 'XMLHttpRequest' }
		});
		if (!response.ok) throw new ApiError('ExportError', 'Failed to export products');
		return response;
	}

	async importProducts(file: File) {
		const formData = new FormData();
		formData.append('file', file);
		const response = await fetch(`${API_BASE}/products/import`, {
			method: 'POST',
			credentials: 'include',
			cache: 'no-store',
			headers: { 'X-Requested-With': 'XMLHttpRequest' },
			body: formData
		});
		if (!response.ok) {
			let error: { error?: string; message?: string } = {};
			try {
				error = await response.json();
			} catch {
				// Non-JSON error body — fall through to the generic message below
			}
			throw new ApiError(error.error || 'ImportError', error.message || 'Failed to import products');
		}
		return response.json() as Promise<ImportProductsResponse>;
	}

	/** The whole account as one JSON file. Credentials are never included (see its `excluded`). */
	async exportBackup() {
		const response = await fetch(`${API_BASE}/account/backup`, {
			credentials: 'include',
			cache: 'no-store',
			headers: { 'X-Requested-With': 'XMLHttpRequest' }
		});
		if (!response.ok) throw new ApiError('ExportError', 'Failed to download backup');
		return response;
	}

	/** Merge-only restore: adds what the account lacks, never deletes or overwrites. */
	async importBackup(file: File) {
		const formData = new FormData();
		formData.append('file', file);
		const response = await fetch(`${API_BASE}/account/backup`, {
			method: 'POST',
			credentials: 'include',
			cache: 'no-store',
			headers: { 'X-Requested-With': 'XMLHttpRequest' },
			body: formData
		});
		if (!response.ok) {
			let body: { error?: string; message?: string } = {};
			try {
				body = await response.json();
			} catch {
				// non-JSON body (e.g. 413 from the size limit)
			}
			// Minimal-API 400s put the human text in `error`; ApiException responses use `message`.
			const message =
				body.message ??
				body.error ??
				(response.status === 413 ? 'The backup file is larger than this server accepts.' : 'Failed to restore backup');
			throw new ApiError('ImportError', message);
		}
		return response.json() as Promise<BackupImportResult>;
	}
}

export const api = new ApiClient();

export interface BackupImportResult {
	settingsRestored: boolean;
	tagsAdded: number;
	comparisonGroupsAdded: number;
	storesAdded: number;
	storesKept: number;
	productsAdded: number;
	productsSkipped: number;
	pricePointsAdded: number;
	alertsAdded: number;
	alertsPaused: number;
	warnings: string[];
}

// Types — enum unions hand-mirrored from backend.
//
// Wire format is camelCase (lower first letter, preserve rest) via Enum.ToApiString() in
// src/Ophi.Domain/Extensions/EnumExtensions.cs. Sources of truth:
//   ProductStatus  → src/Ophi.Domain/Enums/ProductStatus.cs
//   AlertCondition → src/Ophi.Domain/Enums/AlertCondition.cs
//   WebhookEvent   → src/Ophi.Infrastructure/Webhooks/IWebhookDispatchService.cs (WebhookEvents constants)
//   ApiKeyScope    → src/Ophi.Api/Features/ApiKeys (read/write literals)
//
// The .NET OpenAPI generator emits these fields as plain `string` (no enum constraint),
// so we hand-mirror the unions here. Keep these in sync with the backend source files above.
export type ProductStatus = 'active' | 'paused' | 'error' | 'pending';
export type AlertCondition = 'below' | 'above' | 'percentDrop';

export interface GetProductsParams {
	tagId?: string;
	search?: string;
	status?: ProductStatus;
	sortBy?: string;
	sortDirection?: string;
	page?: number;
	pageSize?: number;
	includeSparkline?: boolean;
	atLowest?: boolean;
	favourite?: boolean;
	priceDrop?: boolean;
	hasAlerts?: boolean;
}

export interface CustomField {
	name: string;
	value: string;
}

export interface ProductTag {
	id: string;
	name: string;
	color: string;
}

export interface Product {
	id: string;
	name: string;
	url: string;
	imageUrl?: string;
	currentPrice?: number;
	previousPrice?: number;
	priceChange?: number;
	currency: string;
	lastChecked?: string;
	status: ProductStatus;
	isFavourite: boolean;
	isOutOfStock: boolean;
	storeCount: number;
	alertCount: number;
	tags: ProductTag[];
	customFields: CustomField[];
	sparkline?: { date: string; price: number }[];
	priceMin?: number;
	priceMax?: number;
	dealScore?: number;
	affiliateUrl?: string;
	checkIntervalMinutes?: number;
	hasPriceAnomaly?: boolean;
}

export interface ProductUrl {
	id: string;
	url: string;
	affiliateUrl?: string;
	storeId?: string;
	currentPrice?: number;
	currency: string;
	lastCheckedAt?: string;
	lastError?: string;
	failureCount: number;
	status: string;
	suspiciousReason?: string;
	isOutOfStock: boolean;
}

export interface ProductDetail extends Product {
	urls: ProductUrl[];
	comparisonGroupId?: string;
	hasCurrencyMismatch: boolean;
	statistics: {
		min: number;
		max: number;
		average: number;
		current?: number;
	};
	alerts: ProductAlert[];
}

export interface ProductAlert {
	id: string;
	targetPrice: number;
	condition: AlertCondition;
	active: boolean;
	lastTriggered?: string;
	/** Denomination of `targetPrice` — not always the product's currency. Format the target with this. */
	currency: string;
	/** True when the target's currency no longer matches the product's, so the alert is dormant. */
	hasCurrencyMismatch: boolean;
}

export interface PriceHistory {
	productId: string;
	productName: string;
	history: { date: string; price: number }[];
	urlHistories?: UrlHistory[];
	hasCurrencyMismatch: boolean;
	statistics: {
		min: number;
		max: number;
		average: number;
		current?: number;
	};
}

export interface UrlHistory {
	productUrlId: string;
	url: string;
	currency: string;
	history: PriceHistoryPoint[];
}

export interface Alert {
	id: string;
	productId: string;
	productName: string;
	currentPrice?: number;
	targetPrice: number;
	condition: AlertCondition;
	active: boolean;
	lastTriggered?: string;
	/** Denomination of `targetPrice`. */
	currency: string;
	/** Denomination of `currentPrice`. */
	productCurrency: string;
	/** True when the two differ and the alert is therefore dormant. */
	hasCurrencyMismatch: boolean;
}

export interface Tag {
	id: string;
	name: string;
	color: string;
	weight: number;
	productCount: number;
}

export interface UpdateTagRequest {
	name?: string;
	color?: string;
	weight?: number;
}

export interface ComparisonGroup {
	id: string;
	name: string;
	productCount: number;
}

export interface ComparisonGroupDetail {
	id: string;
	name: string;
	description?: string;
	products: ComparisonProduct[];
	bestPriceProductId?: string;
	bestPrice?: number;
	updatedAt: string;
}

export interface ComparisonProduct {
	id: string;
	name: string;
	url: string;
	affiliateUrl?: string;
	imageUrl?: string;
	currentPrice?: number;
	currency: string;
	isBestPrice: boolean;
	priceHistory: PriceHistoryPoint[];
	customFields: CustomField[];
}

export interface PriceHistoryPoint {
	date: string;
	price: number;
}

// Store types
export interface StoreSelectors {
	priceSelectors: string[];
	nameSelectors: string[];
	imageSelectors: string[];
	priceRegexPatterns?: string[];
	imageRegexPatterns?: string[];
	priceJsonPaths?: string[];
	nameJsonPaths?: string[];
	imageJsonPaths?: string[];
}

export interface Store {
	id?: string;
	storeId: string;
	name: string;
	domainPatterns: string[];
	selectors: StoreSelectors;
	isBuiltIn: boolean;
	isAutoCreated?: boolean;
	createdAt?: string;
	priceLocale: string;
	requiresJavaScript: boolean;
	currencyOverride?: string;
	affiliateParamName?: string;
	affiliateTag?: string;
	customUserAgent?: string;
}

export interface CreateStoreRequest {
	storeId: string;
	name: string;
	domainPatterns: string[];
	selectors: StoreSelectors;
	priceLocale?: string;
	requiresJavaScript?: boolean;
	currencyOverride?: string;
	affiliateParamName?: string;
	affiliateTag?: string;
	customUserAgent?: string;
}

export interface CreateProductRequest {
	name: string;
	imageUrl?: string;
	currency?: string;
	customFields?: CustomField[];
}

export interface UpdateProductRequest {
	name?: string;
	imageUrl?: string;
	status?: ProductStatus;
	isFavourite?: boolean;
	customFields?: CustomField[];
	checkIntervalMinutes?: number | null;
}

export interface UpdateStoreRequest {
	name: string;
	domainPatterns: string[];
	selectors: StoreSelectors;
	priceLocale?: string;
	requiresJavaScript?: boolean;
	currencyOverride?: string;
	affiliateParamName?: string;
	affiliateTag?: string;
	customUserAgent?: string;
}

export interface TestStoreResult {
	success: boolean;
	extractedName?: string;
	extractedPrice?: number;
	extractedImageUrl?: string;
	currency?: string;
	detectedSelector?: string;
	error?: string;
}

export interface Notification {
	id: string;
	title: string;
	message: string;
	type: string;
	isRead: boolean;
	productId?: string;
	productName?: string;
	createdAt: string;
}

export interface ScrapeLogEntry {
	id: string;
	success: boolean;
	price?: number;
	error?: string;
	durationMs: number;
	url?: string;
	isOutOfStock: boolean;
	storeDomain: string;
	createdAt: string;
}

export interface ScrapeHealthResponse {
	domains: DomainHealth[];
	summary: ScrapeHealthSummary;
}

export interface DomainHealth {
	domain: string;
	totalScrapes: number;
	successCount: number;
	failureCount: number;
	successRate: number;
	scrapesToday: number;
	p50DurationMs: number;
	p95DurationMs: number;
	lastFailureMessage?: string;
	lastFailureAt?: string;
	lastScrapeAt?: string;
}

export interface ScrapeHealthSummary {
	totalDomains: number;
	totalScrapes7d: number;
	/** Null when no scrapes fall in the window — render as "no data", not 100%. `0` is a real all-failed rate. */
	overallSuccessRate: number | null;
	domainsHealthy: number;
	domainsDegraded: number;
	domainsUnhealthy: number;
}

export interface StoreExport {
	version: number;
	storeId: string;
	name: string;
	domainPatterns: string[];
	selectors: StoreSelectors;
	priceLocale: string;
	requiresJavaScript: boolean;
	currencyOverride?: string;
}

export interface DetectStoreResult {
	success: boolean;
	storeName?: string;
	domain?: string;
	selectors?: {
		priceSelectors: string[];
		nameSelectors: string[];
		imageSelectors: string[];
		priceRegexPatterns?: string[];
		priceJsonPaths?: string[];
		nameJsonPaths?: string[];
		imageJsonPaths?: string[];
	};
	error?: string;
}

export interface StoreImportRequest {
	storeId: string;
	name: string;
	domainPatterns: string[];
	selectors: StoreSelectors;
	priceLocale?: string;
	requiresJavaScript?: boolean;
	currencyOverride?: string;
}

export interface Settings {
	affiliatesEnabled: boolean;
	defaultCheckIntervalMinutes?: number;
	pageFetchDelaySeconds?: number;
	scrapeCacheTtlMinutes?: number;
	discordWebhookConfigured: boolean;
	discordNotificationsEnabled: boolean;
	anomalyThresholdPercent?: number;
	autoPauseAfterFailures?: number;
	/** Whether the server has SMTP set up. Server-level config — no credentials are exposed. */
	emailConfigured: boolean;
	/** The account's own opt-in for alert email. Independent of `emailConfigured`. */
	emailNotificationsEnabled: boolean;
	/** The server has a Telegram bot token. Hide the Telegram card otherwise. */
	telegramAvailable: boolean;
	/** Bot to message for a chat id; null when the operator didn't set TELEGRAM_BOT_USERNAME. */
	telegramBotUsername?: string | null;
	/** A chat id is saved. The id itself is never returned. */
	telegramConfigured: boolean;
	telegramNotificationsEnabled: boolean;
	pushoverAvailable: boolean;
	/** A user key is saved. The key itself is never returned. */
	pushoverConfigured: boolean;
	pushoverNotificationsEnabled: boolean;
	/** A topic URL is saved. The URL itself is never returned. ntfy needs no server setup. */
	ntfyConfigured: boolean;
	ntfyNotificationsEnabled: boolean;
	/** ISO code prices are also shown in (≈ …). Null = off. */
	displayCurrency?: string | null;
}

export interface FxRates {
	base: string;
	/** ECB publication date; null until the worker has fetched once. */
	asOf?: string | null;
	/** Units per 1 EUR. A currency missing here gets no conversion. */
	rates: Record<string, number>;
	/** Currencies a user may pick as display currency. */
	supported: string[];
}

export type PushChannel = 'telegram' | 'pushover' | 'ntfy';

export interface SendTestEmailResponse {
	success: boolean;
	error?: string;
	/** The signed-in user's own address; the API never accepts a recipient from the caller. */
	sentTo?: string;
}

export interface UpdateSettingsRequest {
	affiliatesEnabled?: boolean;
	defaultCheckIntervalMinutes?: number;
	pageFetchDelaySeconds?: number;
	scrapeCacheTtlMinutes?: number;
	discordWebhookUrl?: string;
	discordNotificationsEnabled?: boolean;
	anomalyThresholdPercent?: number;
	autoPauseAfterFailures?: number;
	emailNotificationsEnabled?: boolean;
	/** "" clears it. */
	telegramChatId?: string;
	telegramNotificationsEnabled?: boolean;
	/** "" clears it. */
	pushoverUserKey?: string;
	pushoverNotificationsEnabled?: boolean;
	/** "" clears it. */
	ntfyTopicUrl?: string;
	ntfyNotificationsEnabled?: boolean;
	/** "" turns conversion off. */
	displayCurrency?: string;
}

export interface TestDiscordWebhookResponse {
	success: boolean;
	error?: string;
}

export interface SetStoreAffiliateRequest {
	affiliateParamName?: string;
	affiliateTag?: string;
}

export type WebhookEvent = 'alert_fired' | 'price_changed' | 'scrape_failed';

export interface WebhookTarget {
	id: string;
	name: string;
	url: string;
	events: WebhookEvent[];
	isEnabled: boolean;
	createdAt: string;
}

export interface CreateWebhookTargetRequest {
	name: string;
	url: string;
	events: WebhookEvent[];
	isEnabled?: boolean;
}

export interface UpdateWebhookTargetRequest {
	name: string;
	url: string;
	events: WebhookEvent[];
	isEnabled: boolean;
}

export interface TestWebhookTargetResponse {
	success: boolean;
	error?: string;
}

export type ApiKeyScope = 'read' | 'write';

export interface ApiKeyInfo {
	id: string;
	name: string;
	scopes: ApiKeyScope[];
	lastUsedAt?: string;
	expiresAt?: string;
	createdAt: string;
}

export interface ApiKeyCreatedResponse {
	id: string;
	name: string;
	scopes: ApiKeyScope[];
	key: string;
	expiresAt?: string;
	createdAt: string;
}

export interface CreateApiKeyRequest {
	name: string;
	scopes: ApiKeyScope[];
	expiresAt?: string;
}

export interface ImportProductsResponse {
	added: number;
	skipped: number;
	errors: string[];
}
