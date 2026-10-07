import { describe, it, expect, vi, beforeEach } from 'vitest';
import { api, ApiError } from './client';

describe('ApiClient', () => {
	beforeEach(() => {
		vi.resetAllMocks();
	});

	describe('register', () => {
		it('should call register endpoint with correct data', async () => {
			const mockResponse = { id: '123', email: 'test@example.com', name: 'Test User' };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.register('test@example.com', 'password123', 'Test User');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/auth/register'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({
						email: 'test@example.com',
						password: 'password123',
						name: 'Test User'
					})
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should throw error on failed registration', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: false,
				status: 400,
				json: () => Promise.resolve({ error: 'ValidationError', message: 'Invalid email' })
			} as Response);

			await expect(api.register('invalid', 'password', 'name')).rejects.toThrow('Invalid email');
		});
	});

	describe('error handling', () => {
		it('should throw ApiError with generic message when response is non-JSON', async () => {
			// Simulates a 502 gateway error returning an HTML page
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: false,
				status: 502,
				json: () => Promise.reject(new SyntaxError('Unexpected token'))
			} as unknown as Response);

			const error = await api.login('test@example.com', 'password').catch((e) => e);

			expect(error).toBeInstanceOf(ApiError);
			expect(error.code).toBe('UnknownError');
			expect(error.message).toBe('An error occurred');
		});
	});

	describe('login', () => {
		it('should call login endpoint with correct data', async () => {
			const mockResponse = { id: '123', email: 'test@example.com', name: 'Test User' };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.login('test@example.com', 'password123');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/auth/login'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ email: 'test@example.com', password: 'password123' })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should throw error on invalid credentials', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: false,
				status: 401,
				json: () => Promise.resolve({ error: 'Unauthorized', message: 'Invalid credentials' })
			} as Response);

			await expect(api.login('test@example.com', 'wrong')).rejects.toThrow('Invalid credentials');
		});
	});

	describe('logout', () => {
		it('should call logout endpoint', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.logout();

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/auth/logout'),
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('changePassword', () => {
		it('should call account password endpoint with correct data', async () => {
			const mockResponse = { message: 'Password changed. Other sessions have been signed out.' };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.changePassword('OldPassword1', 'NewPassword1');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/account/password'),
				expect.objectContaining({
					method: 'PUT',
					body: JSON.stringify({ currentPassword: 'OldPassword1', newPassword: 'NewPassword1' })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should throw error when current password is wrong', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: false,
				status: 401,
				json: () =>
					Promise.resolve({ error: 'Unauthorized', message: 'Current password is incorrect' })
			} as Response);

			await expect(api.changePassword('wrong', 'NewPassword1')).rejects.toThrow(
				'Current password is incorrect'
			);
		});
	});

	describe('updateProfile', () => {
		it('should call account profile endpoint with name and email', async () => {
			const mockResponse = { id: '123', email: 'new@example.com', name: 'New Name' };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.updateProfile({ name: 'New Name', email: 'new@example.com' });

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/account/profile'),
				expect.objectContaining({
					method: 'PUT',
					body: JSON.stringify({ name: 'New Name', email: 'new@example.com' })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should include currentPassword when provided', async () => {
			const mockResponse = { id: '123', email: 'new@example.com', name: 'New Name' };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.updateProfile({
				name: 'New Name',
				email: 'new@example.com',
				currentPassword: 'Password1'
			});

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/account/profile'),
				expect.objectContaining({
					body: JSON.stringify({
						name: 'New Name',
						email: 'new@example.com',
						currentPassword: 'Password1'
					})
				})
			);
		});

		it('should throw error when email is already in use', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: false,
				status: 409,
				json: () => Promise.resolve({ error: 'EmailInUse', message: 'This email is already in use' })
			} as Response);

			const error = await api
				.updateProfile({ name: 'Name', email: 'taken@example.com', currentPassword: 'Password1' })
				.catch((e) => e);

			expect(error).toBeInstanceOf(ApiError);
			expect(error.code).toBe('EmailInUse');
		});
	});

	describe('deleteAccount', () => {
		it('should call account endpoint with DELETE and password', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.deleteAccount('Password1');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/account'),
				expect.objectContaining({
					method: 'DELETE',
					body: JSON.stringify({ password: 'Password1' })
				})
			);
		});

		it('should throw error when password is wrong', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: false,
				status: 401,
				json: () => Promise.resolve({ error: 'Unauthorized', message: 'Password is incorrect' })
			} as Response);

			await expect(api.deleteAccount('wrong')).rejects.toThrow('Password is incorrect');
		});
	});

	describe('getProducts', () => {
		it('should call products endpoint', async () => {
			const mockResponse = { items: [], total: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getProducts();

			expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/products'), expect.any(Object));
			expect(result).toEqual(mockResponse);
		});

		it('should include tagId query param when provided', async () => {
			const mockResponse = { items: [], total: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.getProducts({ tagId: 'tag-123' });

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('?tagId=tag-123'),
				expect.any(Object)
			);
		});

		it('should include search query param when provided', async () => {
			const mockResponse = { items: [], total: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.getProducts({ search: 'headphones' });

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('search=headphones'),
				expect.any(Object)
			);
		});

		it('should include status query param when provided', async () => {
			const mockResponse = { items: [], total: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.getProducts({ status: 'active' });

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('status=active'),
				expect.any(Object)
			);
		});

		it('should include sortBy and sortDirection params when provided', async () => {
			const mockResponse = { items: [], total: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.getProducts({ sortBy: 'price', sortDirection: 'desc' });

			const calledUrl = vi.mocked(fetch).mock.calls[0][0] as string;
			expect(calledUrl).toContain('sortBy=price');
			expect(calledUrl).toContain('sortDirection=desc');
		});

		it('should include all params when provided', async () => {
			const mockResponse = { items: [], total: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.getProducts({
				tagId: 'tag-1',
				search: 'sony',
				status: 'active',
				sortBy: 'name',
				sortDirection: 'asc'
			});

			const calledUrl = vi.mocked(fetch).mock.calls[0][0] as string;
			expect(calledUrl).toContain('tagId=tag-1');
			expect(calledUrl).toContain('search=sony');
			expect(calledUrl).toContain('status=active');
			expect(calledUrl).toContain('sortBy=name');
			expect(calledUrl).toContain('sortDirection=asc');
		});
	});

	describe('addProduct', () => {
		it('should call add product endpoint with URL', async () => {
			const mockResponse = {
				id: '123',
				name: 'Test Product',
				url: 'https://example.com/product',
				currency: 'USD',
				status: 'active'
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 201,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.addProduct('https://example.com/product');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ url: 'https://example.com/product' })
				})
			);
			expect(result).toEqual(mockResponse);
		});
	});

	describe('deleteProduct', () => {
		it('should call delete endpoint with product id', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.deleteProduct('product-123');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123'),
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('getPriceHistory', () => {
		it('should call price history endpoint', async () => {
			const mockResponse = {
				productId: '123',
				productName: 'Test',
				history: [],
				statistics: { min: 0, max: 0, average: 0 }
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getPriceHistory('product-123', 30);

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/history?days=30'),
				expect.any(Object)
			);
			expect(result).toEqual(mockResponse);
		});
	});

	describe('addProductUrl', () => {
		it('should call add product URL endpoint with correct data', async () => {
			const mockResponse = {
				id: 'url-123',
				url: 'https://store2.com/product',
				currency: 'USD',
				failureCount: 0,
				status: 'active'
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 201,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.addProductUrl('product-123', 'https://store2.com/product');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/urls'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ url: 'https://store2.com/product' })
				})
			);
			expect(result).toEqual(mockResponse);
		});
	});

	describe('removeProductUrl', () => {
		it('should call remove product URL endpoint with correct ids', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.removeProductUrl('product-123', 'url-456');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/urls/url-456'),
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('retryScrapeProductUrl', () => {
		it('should resolve when the server returns 202 with an empty body', async () => {
			// Results.Accepted() returns 202 with no body; response.json() rejects
			// on the empty body. The client must not surface that as a failure.
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 202,
				json: () => Promise.reject(new SyntaxError('Unexpected end of JSON input'))
			} as unknown as Response);

			await expect(
				api.retryScrapeProductUrl('product-123', 'url-456')
			).resolves.not.toThrow();

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/urls/url-456/retry'),
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('resumeProductUrl', () => {
		it('should call resume product URL endpoint with correct ids', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve({})
			} as Response);

			await api.resumeProductUrl('product-123', 'url-456');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/urls/url-456/resume'),
				expect.objectContaining({ method: 'POST' })
			);
		});
	});

	describe('alerts', () => {
		it('should get alerts', async () => {
			const mockResponse = { items: [] };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getAlerts();

			expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/alerts'), expect.any(Object));
			expect(result).toEqual(mockResponse);
		});

		it('should create alert', async () => {
			// Mirrors the real 201 body field for field. It used to be a four-field stub, which is
			// how the Alert interface came to declare productName/currency/productCurrency/
			// hasCurrencyMismatch as required while CreateAlert.Response returned none of them —
			// nothing in the type system compares a mock against the endpoint that replaced it.
			const mockResponse = {
				id: '123',
				productId: 'p1',
				productName: 'Test Product',
				currentPrice: 60,
				targetPrice: 50,
				condition: 'below',
				active: true,
				createdAt: '2026-01-01T00:00:00Z',
				currency: 'USD',
				productCurrency: 'USD',
				hasCurrencyMismatch: false
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 201,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.createAlert('p1', 50, 'below');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/alerts'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ productId: 'p1', targetPrice: 50, condition: 'below' })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should redenominate alert', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () =>
					Promise.resolve({
						id: 'alert-123',
						productId: 'p1',
						productName: 'Test Product',
						targetPrice: 69,
						condition: 'below',
						active: true,
						currency: 'EUR',
						productCurrency: 'EUR',
						hasCurrencyMismatch: false
					})
			} as Response);

			const result = await api.redenominateAlert('alert-123', 69, 'EUR');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/alerts/alert-123/redenominate'),
				expect.objectContaining({
					method: 'POST',
					// The currency the user was shown travels with the target, so the server can
					// refuse a target picked against a currency the product has since left.
					body: JSON.stringify({ targetPrice: 69, expectedCurrency: 'EUR' })
				})
			);
			expect(result.hasCurrencyMismatch).toBe(false);
		});

		it('should delete alert', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.deleteAlert('alert-123');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/alerts/alert-123'),
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('tags', () => {
		it('should get tags', async () => {
			const mockResponse = { items: [] };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getTags();

			expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/tags'), expect.any(Object));
			expect(result).toEqual(mockResponse);
		});

		it('should create tag', async () => {
			const mockResponse = { id: '123', name: 'Electronics', color: '#3B82F6', weight: 0, productCount: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 201,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.createTag('Electronics', '#3B82F6', 0);

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/tags'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ name: 'Electronics', color: '#3B82F6', weight: 0 })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should add tag to product', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.addTagToProduct('product-123', 'tag-456');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/tags'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ tagId: 'tag-456' })
				})
			);
		});

		it('should remove tag from product', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.removeTagFromProduct('product-123', 'tag-456');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/products/product-123/tags/tag-456'),
				expect.objectContaining({
					method: 'DELETE'
				})
			);
		});
	});

	describe('stores', () => {
		it('should call testStore endpoint with correct data', async () => {
			const mockResponse = {
				success: true,
				extractedName: 'Test Product',
				extractedPrice: 29.99,
				currency: 'USD',
				extractedImageUrl: 'https://example.com/img.jpg',
				detectedSelector: '.price'
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.testStore('my-store', 'https://example.com/product/1');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/stores/test'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ storeId: 'my-store', testUrl: 'https://example.com/product/1' })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should handle testStore failure response', async () => {
			const mockResponse = {
				success: false,
				error: 'Could not extract price from page'
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.testStore('my-store', 'https://example.com/bad');

			expect(result.success).toBe(false);
			expect(result.error).toBe('Could not extract price from page');
		});
	});

	describe('comparisons', () => {
		it('should get comparison groups', async () => {
			const mockResponse = { items: [] };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getComparisonGroups();

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons'),
				expect.any(Object)
			);
			expect(result).toEqual(mockResponse);
		});

		it('should get comparison group detail', async () => {
			const mockResponse = {
				id: '123',
				name: 'Headphones',
				products: [],
				bestPriceProductId: null,
				bestPrice: null,
				updatedAt: '2024-01-15T00:00:00Z'
			};
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getComparisonGroup('123', 30);

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons/123?days=30'),
				expect.any(Object)
			);
			expect(result).toEqual(mockResponse);
		});

		it('should create comparison group', async () => {
			const mockResponse = { id: '123', name: 'Headphones Comparison', productCount: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 201,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.createComparisonGroup('Headphones Comparison', 'Compare headphones');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ name: 'Headphones Comparison', description: 'Compare headphones' })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should create comparison group without description', async () => {
			const mockResponse = { id: '123', name: 'Headphones Comparison', productCount: 0 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 201,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.createComparisonGroup('Headphones Comparison');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ name: 'Headphones Comparison', description: undefined })
				})
			);
			expect(result).toEqual(mockResponse);
		});

		it('should delete comparison group', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.deleteComparisonGroup('group-123');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons/group-123'),
				expect.objectContaining({ method: 'DELETE' })
			);
		});

		it('should add product to comparison group', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve({})
			} as Response);

			await api.addProductToComparisonGroup('group-123', 'product-456');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons/group-123/products'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ productId: 'product-456' })
				})
			);
		});

		it('should add multiple products to comparison group in one batch request', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.addProductsToComparisonGroup('group-123', ['product-1', 'product-2']);

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons/group-123/products/batch'),
				expect.objectContaining({
					method: 'POST',
					body: JSON.stringify({ productIds: ['product-1', 'product-2'] })
				})
			);
		});

		it('should remove product from comparison group', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.removeProductFromComparisonGroup('group-123', 'product-456');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/comparisons/group-123/products/product-456'),
				expect.objectContaining({ method: 'DELETE' })
			);
		});
	});

	describe('notifications', () => {
		it('should get notifications', async () => {
			const mockResponse = { items: [] };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getNotifications();

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/notifications'),
				expect.any(Object)
			);
			expect(result).toEqual(mockResponse);
		});

		it('should get notifications with unreadOnly filter', async () => {
			const mockResponse = { items: [] };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			await api.getNotifications({ unreadOnly: true });

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('unreadOnly=true'),
				expect.any(Object)
			);
		});

		it('should get notification count', async () => {
			const mockResponse = { unread: 5 };
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 200,
				json: () => Promise.resolve(mockResponse)
			} as Response);

			const result = await api.getNotificationCount();

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/notifications/count'),
				expect.any(Object)
			);
			expect(result).toEqual(mockResponse);
		});

		it('should mark notification as read', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.markNotificationRead('notif-123');

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/notifications/notif-123/read'),
				expect.objectContaining({ method: 'PUT' })
			);
		});

		it('should mark all notifications as read', async () => {
			vi.mocked(fetch).mockResolvedValueOnce({
				ok: true,
				status: 204,
				json: () => Promise.resolve({})
			} as Response);

			await api.markAllNotificationsRead();

			expect(fetch).toHaveBeenCalledWith(
				expect.stringContaining('/notifications/read-all'),
				expect.objectContaining({ method: 'POST' })
			);
		});
	});
});
