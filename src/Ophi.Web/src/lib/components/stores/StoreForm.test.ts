import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import StoreForm from './StoreForm.svelte';
import { ApiError } from '$lib/api/client';
import type { Store } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual<typeof import('$lib/api/client')>('$lib/api/client');
	return {
		...actual,
		api: {
			detectStore: vi.fn()
		}
	};
});

describe('StoreForm', () => {
	const mockStore: Store = {
		id: 'store-1',
		storeId: 'test-store',
		name: 'Test Store',
		domainPatterns: ['example.com'],
		selectors: {
			priceSelectors: ['.price'],
			nameSelectors: ['.name'],
			imageSelectors: ['.image'],
			priceRegexPatterns: [],
			imageRegexPatterns: []
		},
		isBuiltIn: false,
		priceLocale: 'en-US',
		requiresJavaScript: false
	};

	const defaultProps = {
		onSubmit: vi.fn(),
		onCancel: vi.fn()
	};

	describe('rendering - create mode', () => {
		it('should render Store ID input in create mode', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByLabelText(/Store ID/)).toBeInTheDocument();
		});

		it('should render Display Name input', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByLabelText(/Display Name/)).toBeInTheDocument();
		});

		it('should render all section headers', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByText('Auto-detect from URL')).toBeInTheDocument();
			expect(screen.getByText('Basic Information')).toBeInTheDocument();
			expect(screen.getByText('Domain Patterns')).toBeInTheDocument();
			expect(screen.getByText('CSS Selectors')).toBeInTheDocument();
			// Regex Patterns header has "(Optional)" suffix
			expect(
				screen.getByText(
					(content, element) => element?.tagName === 'H4' && content.includes('Regex Patterns')
				)
			).toBeInTheDocument();
		});

		it('should render selector inputs for price, name, and image', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByText('Price Selectors')).toBeInTheDocument();
			expect(screen.getByText('Name Selectors')).toBeInTheDocument();
			expect(screen.getByText('Image Selectors')).toBeInTheDocument();
		});

		it('should render cancel and submit buttons', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
			expect(screen.getByRole('button', { name: 'Create Store' })).toBeInTheDocument();
		});
	});

	describe('rendering - edit mode', () => {
		it('should not render Store ID input in edit mode', () => {
			render(StoreForm, { props: { ...defaultProps, store: mockStore } });
			expect(screen.queryByLabelText(/Store ID/)).not.toBeInTheDocument();
		});

		it('should show "Update Store" button in edit mode', () => {
			render(StoreForm, { props: { ...defaultProps, store: mockStore } });
			expect(screen.getByRole('button', { name: 'Update Store' })).toBeInTheDocument();
		});

		it('should pre-fill form with store data', () => {
			render(StoreForm, { props: { ...defaultProps, store: mockStore } });
			const nameInput = screen.getByLabelText(/Display Name/) as HTMLInputElement;
			expect(nameInput.value).toBe('Test Store');
		});

		it('should not render auto-detect section in edit mode', () => {
			render(StoreForm, { props: { ...defaultProps, store: mockStore } });
			expect(screen.queryByText('Auto-detect from URL')).not.toBeInTheDocument();
			expect(screen.queryByTestId('detect-url-input')).not.toBeInTheDocument();
			expect(screen.queryByTestId('detect-button')).not.toBeInTheDocument();
		});
	});

	describe('auto-detect', () => {
		it('should render auto-detect section in create mode', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByText('Auto-detect from URL')).toBeInTheDocument();
			expect(screen.getByTestId('detect-url-input')).toBeInTheDocument();
			expect(screen.getByTestId('detect-button')).toBeInTheDocument();
		});

		it('should show error when detect is clicked with empty URL', async () => {
			render(StoreForm, { props: defaultProps });

			await fireEvent.click(screen.getByTestId('detect-button'));

			expect(screen.getByTestId('detect-error')).toHaveTextContent('Please enter a URL');
		});

		it('should call detectStore API and populate form on success', async () => {
			const { api } = await import('$lib/api/client');
			const detectStoreMock = vi.mocked(api.detectStore);
			detectStoreMock.mockResolvedValueOnce({
				success: true,
				storeName: 'Example Shop',
				domain: 'shop.example.com',
				selectors: {
					priceSelectors: ['.price', '[data-price]'],
					nameSelectors: ['h1'],
					imageSelectors: ["meta[property='og:image']|content"]
				}
			});

			render(StoreForm, { props: defaultProps });

			const urlInput = screen.getByTestId('detect-url-input');
			await fireEvent.input(urlInput, {
				target: { value: 'https://shop.example.com/product/1' }
			});
			await fireEvent.click(screen.getByTestId('detect-button'));

			await waitFor(() => {
				expect(screen.getByTestId('detect-success')).toBeInTheDocument();
			});

			expect(detectStoreMock).toHaveBeenCalledWith('https://shop.example.com/product/1');

			// Check form fields were populated
			const nameInput = screen.getByLabelText(/Display Name/) as HTMLInputElement;
			expect(nameInput.value).toBe('Example Shop');

			const storeIdInput = screen.getByLabelText(/Store ID/) as HTMLInputElement;
			expect(storeIdInput.value).toBe('shop-example-com');
		});

		it('should show error on failed detection', async () => {
			const { api } = await import('$lib/api/client');
			const detectStoreMock = vi.mocked(api.detectStore);
			detectStoreMock.mockResolvedValueOnce({
				success: false,
				error: 'Could not detect store selectors from the page'
			});

			render(StoreForm, { props: defaultProps });

			const urlInput = screen.getByTestId('detect-url-input');
			await fireEvent.input(urlInput, {
				target: { value: 'https://example.com/not-a-product' }
			});
			await fireEvent.click(screen.getByTestId('detect-button'));

			await waitFor(() => {
				expect(screen.getByRole('alert')).toHaveTextContent(
					'Could not detect store selectors from the page'
				);
			});
		});

		it('should show error on API exception', async () => {
			const { api } = await import('$lib/api/client');
			const detectStoreMock = vi.mocked(api.detectStore);
			detectStoreMock.mockRejectedValueOnce(new Error('Network error'));

			render(StoreForm, { props: defaultProps });

			const urlInput = screen.getByTestId('detect-url-input');
			await fireEvent.input(urlInput, { target: { value: 'https://example.com' } });
			await fireEvent.click(screen.getByTestId('detect-button'));

			await waitFor(() => {
				expect(screen.getByTestId('detect-error')).toHaveTextContent('Network error');
			});
		});
	});

	describe('Store ID validation', () => {
		it('should show error for empty Store ID', async () => {
			const onSubmit = vi.fn();
			render(StoreForm, { props: { ...defaultProps, onSubmit } });

			// Fill in other required fields but leave Store ID empty
			const nameInput = screen.getByLabelText(/Display Name/);
			await fireEvent.input(nameInput, { target: { value: 'My Store' } });

			const form = screen.getByRole('button', { name: 'Create Store' }).closest('form')!;
			await fireEvent.submit(form);

			expect(screen.getByText('Store ID is required')).toHaveAttribute('role', 'alert');
			expect(onSubmit).not.toHaveBeenCalled();
		});

		it('should show error for Store ID less than 3 characters', async () => {
			const onSubmit = vi.fn();
			render(StoreForm, { props: { ...defaultProps, onSubmit } });

			const storeIdInput = screen.getByLabelText(/Store ID/);
			await fireEvent.input(storeIdInput, { target: { value: 'ab' } });

			const nameInput = screen.getByLabelText(/Display Name/);
			await fireEvent.input(nameInput, { target: { value: 'My Store' } });

			const form = screen.getByRole('button', { name: 'Create Store' }).closest('form')!;
			await fireEvent.submit(form);

			expect(screen.getByText('Store ID must be at least 3 characters')).toBeInTheDocument();
		});

		it('should show error for invalid Store ID characters', async () => {
			const onSubmit = vi.fn();
			render(StoreForm, { props: { ...defaultProps, onSubmit } });

			const storeIdInput = screen.getByLabelText(/Store ID/);
			await fireEvent.input(storeIdInput, { target: { value: 'My Store' } });

			const nameInput = screen.getByLabelText(/Display Name/);
			await fireEvent.input(nameInput, { target: { value: 'My Store' } });

			const form = screen.getByRole('button', { name: 'Create Store' }).closest('form')!;
			await fireEvent.submit(form);

			expect(
				screen.getByText('Store ID can only contain lowercase letters, numbers, and hyphens')
			).toBeInTheDocument();
		});

		it('should show error for reserved Store ID', async () => {
			const onSubmit = vi.fn();
			render(StoreForm, { props: { ...defaultProps, onSubmit } });

			const storeIdInput = screen.getByLabelText(/Store ID/);
			await fireEvent.input(storeIdInput, { target: { value: 'amazon' } });

			const nameInput = screen.getByLabelText(/Display Name/);
			await fireEvent.input(nameInput, { target: { value: 'My Store' } });

			const form = screen.getByRole('button', { name: 'Create Store' }).closest('form')!;
			await fireEvent.submit(form);

			expect(screen.getByText('This ID is reserved for built-in stores')).toBeInTheDocument();
		});
	});

	describe('name validation', () => {
		it('should show error for empty name', async () => {
			const onSubmit = vi.fn();
			render(StoreForm, { props: { ...defaultProps, onSubmit } });

			const storeIdInput = screen.getByLabelText(/Store ID/);
			await fireEvent.input(storeIdInput, { target: { value: 'my-store' } });

			const form = screen.getByRole('button', { name: 'Create Store' }).closest('form')!;
			await fireEvent.submit(form);

			expect(screen.getByText('Name is required')).toBeInTheDocument();
		});
	});

	describe('loading state', () => {
		it('should show loading spinner during submission', async () => {
			let resolveSubmit: () => void;
			const submitPromise = new Promise<void>((resolve) => {
				resolveSubmit = resolve;
			});
			const onSubmit = vi.fn().mockReturnValue(submitPromise);

			const { container } = render(StoreForm, {
				props: { ...defaultProps, store: mockStore, onSubmit }
			});

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			fireEvent.submit(form);

			await waitFor(() => {
				const spinner = container.querySelector('.animate-spin');
				expect(spinner).toBeInTheDocument();
			});

			resolveSubmit!();
		});

		it('should disable inputs during submission', async () => {
			let resolveSubmit: () => void;
			const submitPromise = new Promise<void>((resolve) => {
				resolveSubmit = resolve;
			});
			const onSubmit = vi.fn().mockReturnValue(submitPromise);

			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByLabelText(/Display Name/)).toBeDisabled();
				expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
			});

			resolveSubmit!();
		});
	});

	describe('error handling', () => {
		it('should display error message on submission failure', async () => {
			const onSubmit = vi.fn().mockRejectedValue(new Error('Network error'));
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Network error')).toBeInTheDocument();
			});
		});

		it('should display API error message', async () => {
			const onSubmit = vi
				.fn()
				.mockRejectedValue(new ApiError('VALIDATION_ERROR', 'Store already exists'));
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Store already exists')).toBeInTheDocument();
			});
		});
	});

	describe('currency override', () => {
		it('should render currency override checkbox', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByTestId('currency-override-checkbox')).toBeInTheDocument();
			expect(screen.getByText('Override currency')).toBeInTheDocument();
		});

		it('should not show currency input when checkbox is unchecked', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.queryByTestId('currency-override-input')).not.toBeInTheDocument();
		});

		it('should show currency input when checkbox is checked', async () => {
			render(StoreForm, { props: defaultProps });
			await fireEvent.click(screen.getByTestId('currency-override-checkbox'));
			expect(screen.getByTestId('currency-override-input')).toBeInTheDocument();
		});

		it('should show validation error for invalid currency code', async () => {
			const onSubmit = vi.fn();
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			await fireEvent.click(screen.getByTestId('currency-override-checkbox'));
			const input = screen.getByTestId('currency-override-input');
			await fireEvent.input(input, { target: { value: 'eu' } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			expect(
				screen.getByText('Currency must be a 3-letter uppercase code (e.g., EUR, USD)')
			).toBeInTheDocument();
			expect(onSubmit).not.toHaveBeenCalled();
		});

		it('should include currencyOverride in submission when enabled', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			await fireEvent.click(screen.getByTestId('currency-override-checkbox'));
			const input = screen.getByTestId('currency-override-input');
			await fireEvent.input(input, { target: { value: 'EUR' } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith(
					expect.objectContaining({
						currencyOverride: 'EUR'
					})
				);
			});
		});

		it('should not include currencyOverride when checkbox is unchecked', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg.currencyOverride).toBeUndefined();
			});
		});

		it('should pre-fill currency override from store data', () => {
			const storeWithCurrency = { ...mockStore, currencyOverride: 'GBP' };
			render(StoreForm, { props: { ...defaultProps, store: storeWithCurrency } });

			expect(screen.getByTestId('currency-override-checkbox')).toBeChecked();
			const input = screen.getByTestId('currency-override-input') as HTMLInputElement;
			expect(input.value).toBe('GBP');
		});
	});

	describe('cancel button', () => {
		it('should call onCancel when cancel button is clicked', async () => {
			const onCancel = vi.fn();
			render(StoreForm, { props: { ...defaultProps, onCancel } });

			await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

			expect(onCancel).toHaveBeenCalled();
		});
	});

	describe('JSONPath expressions', () => {
		it('should render JSONPath Expressions section header', () => {
			render(StoreForm, { props: defaultProps });
			expect(
				screen.getByText(
					(content, element) =>
						element?.tagName === 'H4' && content.includes('JSONPath Expressions')
				)
			).toBeInTheDocument();
		});

		it('should render JSONPath input labels', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByText('Price JSONPath')).toBeInTheDocument();
			expect(screen.getByText('Name JSONPath')).toBeInTheDocument();
			expect(screen.getByText('Image JSONPath')).toBeInTheDocument();
		});

		it('should populate JSONPath fields in edit mode', () => {
			const storeWithJsonPaths: Store = {
				...mockStore,
				selectors: {
					...mockStore.selectors,
					priceJsonPaths: ['$.offers.price'],
					nameJsonPaths: ['$.name'],
					imageJsonPaths: ['$.image']
				}
			};
			render(StoreForm, { props: { ...defaultProps, store: storeWithJsonPaths } });

			// The SelectorListInput renders inputs with the values
			const inputs = screen.getAllByRole('textbox') as HTMLInputElement[];
			const values = inputs.map((i) => i.value);
			expect(values).toContain('$.offers.price');
			expect(values).toContain('$.name');
			expect(values).toContain('$.image');
		});

		it('should include JSONPath data in submit payload', async () => {
			const storeWithJsonPaths: Store = {
				...mockStore,
				selectors: {
					...mockStore.selectors,
					priceJsonPaths: ['$.offers.price'],
					nameJsonPaths: [],
					imageJsonPaths: []
				}
			};
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: storeWithJsonPaths, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg.selectors.priceJsonPaths).toEqual(['$.offers.price']);
			});
		});
	});

	describe('form submission', () => {
		it('should call onSubmit with UpdateStoreRequest in edit mode', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const nameInput = screen.getByLabelText(/Display Name/) as HTMLInputElement;
			await fireEvent.input(nameInput, { target: { value: 'Updated Store' } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith(
					expect.objectContaining({
						name: 'Updated Store'
					})
				);
			});
		});

		it('should not include storeId in UpdateStoreRequest', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg).not.toHaveProperty('storeId');
			});
		});
	});

	describe('affiliate configuration', () => {
		it('should render affiliate param name and tag inputs', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByTestId('affiliate-param-name')).toBeInTheDocument();
			expect(screen.getByTestId('affiliate-tag')).toBeInTheDocument();
		});

		it('should populate affiliate fields from store data', async () => {
			const storeWithAffiliate: Store = {
				...mockStore,
				affiliateParamName: 'tag',
				affiliateTag: 'ophi-20'
			};
			render(StoreForm, { props: { ...defaultProps, store: storeWithAffiliate } });

			await waitFor(() => {
				const paramInput = screen.getByTestId('affiliate-param-name') as HTMLInputElement;
				const tagInput = screen.getByTestId('affiliate-tag') as HTMLInputElement;
				expect(paramInput.value).toBe('tag');
				expect(tagInput.value).toBe('ophi-20');
			});
		});

		it('should include affiliate fields in submission when filled', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			const storeWithAffiliate: Store = {
				...mockStore,
				affiliateParamName: 'ref',
				affiliateTag: 'my-tag'
			};
			render(StoreForm, { props: { ...defaultProps, store: storeWithAffiliate, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg.affiliateParamName).toBe('ref');
				expect(callArg.affiliateTag).toBe('my-tag');
			});
		});

		it('should not include affiliate fields when empty', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg.affiliateParamName).toBeUndefined();
				expect(callArg.affiliateTag).toBeUndefined();
			});
		});
	});

	describe('custom user-agent', () => {
		it('should render custom user-agent input in scraping options', () => {
			render(StoreForm, { props: defaultProps });
			expect(screen.getByTestId('custom-user-agent')).toBeInTheDocument();
			expect(screen.getByLabelText(/Custom User-Agent/)).toBeInTheDocument();
		});

		it('should populate custom user-agent from store in edit mode', async () => {
			const storeWithUA: Store = {
				...mockStore,
				customUserAgent: 'Mozilla/5.0 CustomBrowser/1.0'
			};
			render(StoreForm, { props: { ...defaultProps, store: storeWithUA } });

			await waitFor(() => {
				const input = screen.getByTestId('custom-user-agent') as HTMLInputElement;
				expect(input.value).toBe('Mozilla/5.0 CustomBrowser/1.0');
			});
		});

		it('should include customUserAgent in submit payload when set', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const uaInput = screen.getByTestId('custom-user-agent');
			await fireEvent.input(uaInput, { target: { value: 'Mozilla/5.0 TestUA/2.0' } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg.customUserAgent).toBe('Mozilla/5.0 TestUA/2.0');
			});
		});

		it('should omit customUserAgent from payload when blank', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(StoreForm, { props: { ...defaultProps, store: mockStore, onSubmit } });

			const form = screen.getByRole('button', { name: 'Update Store' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalled();
				const callArg = onSubmit.mock.calls[0][0];
				expect(callArg.customUserAgent).toBeUndefined();
			});
		});
	});
});
