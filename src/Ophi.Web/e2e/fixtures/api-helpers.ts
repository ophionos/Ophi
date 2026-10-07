import { APIRequestContext } from '@playwright/test';

/**
 * Shared API helper for E2E tests.
 * All mutating requests include the X-Requested-With header required by CSRF middleware.
 */
export class TestApi {
	constructor(private request: APIRequestContext) {}

	async get<T = unknown>(endpoint: string): Promise<T> {
		const response = await this.request.get(`/api/v1${endpoint}`);
		return response.json();
	}

	async post<T = unknown>(endpoint: string, data?: unknown): Promise<T> {
		const response = await this.request.post(`/api/v1${endpoint}`, {
			data,
			headers: {
				'Content-Type': 'application/json',
				'X-Requested-With': 'XMLHttpRequest'
			}
		});
		if (!response.ok()) {
			throw new Error(`API POST ${endpoint} failed: ${response.status()} ${await response.text()}`);
		}
		const text = await response.text();
		return text ? JSON.parse(text) : ({} as T);
	}

	async delete(endpoint: string): Promise<void> {
		const response = await this.request.delete(`/api/v1${endpoint}`, {
			headers: {
				'X-Requested-With': 'XMLHttpRequest'
			}
		});
		if (!response.ok() && response.status() !== 404) {
			throw new Error(`API DELETE ${endpoint} failed: ${response.status()} ${await response.text()}`);
		}
	}

	async put<T = unknown>(endpoint: string, data?: unknown): Promise<T> {
		const response = await this.request.put(`/api/v1${endpoint}`, {
			data,
			headers: {
				'Content-Type': 'application/json',
				'X-Requested-With': 'XMLHttpRequest'
			}
		});
		if (!response.ok()) {
			throw new Error(`API PUT ${endpoint} failed: ${response.status()} ${await response.text()}`);
		}
		const text = await response.text();
		return text ? JSON.parse(text) : ({} as T);
	}

	// Convenience methods
	async createComparison(name: string, description?: string) {
		return this.post<{ id: string; name: string }>('/comparisons', { name, description });
	}

	async deleteComparison(id: string) {
		return this.delete(`/comparisons/${id}`);
	}

	async addProductToGroup(groupId: string, productId: string) {
		return this.post(`/comparisons/${groupId}/products`, { productId });
	}

	async createProduct(name: string) {
		return this.post<{ id: string }>('/products/create', { name });
	}

	async deleteProduct(id: string) {
		return this.delete(`/products/${id}`);
	}

	async createTag(name: string, color?: string, weight?: number) {
		return this.post<{ id: string; name: string }>('/tags', { name, color, weight });
	}

	async deleteTag(id: string) {
		return this.delete(`/tags/${id}`);
	}

	async getTags() {
		return this.get<{ items: Array<{ id: string; name: string }> }>('/tags');
	}

	async getComparisons() {
		return this.get<{ items: Array<{ id: string; name: string }> }>('/comparisons');
	}

	async listApiKeys() {
		return this.get<Array<{ id: string; name: string }>>('/api-keys');
	}

	async createApiKey(name: string, scopes: string[] = ['read']) {
		return this.post<{ id: string; name: string; key: string }>('/api-keys', { name, scopes });
	}

	async deleteApiKey(id: string) {
		return this.delete(`/api-keys/${id}`);
	}

	async getSettings() {
		return this.get<Record<string, unknown>>('/settings');
	}

	async updateSettings(data: Record<string, unknown>) {
		return this.put('/settings', data);
	}

	async listWebhooks() {
		return this.get<Array<{ id: string; name: string }>>('/webhooks');
	}

	async deleteWebhook(id: string) {
		return this.delete(`/webhooks/${id}`);
	}
}
