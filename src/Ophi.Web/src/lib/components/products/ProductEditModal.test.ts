import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ProductEditModal from './ProductEditModal.svelte';
import type { ProductDetail } from '$lib/api/client';

function createProduct(overrides: Partial<ProductDetail> = {}): ProductDetail {
	return {
		id: '1',
		name: 'Test Product',
		url: 'https://example.com/product',
		currentPrice: 29.99,
		previousPrice: 34.99,
		priceChange: -14.29,
		currency: 'USD',
		lastChecked: '2026-03-19T12:00:00Z',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: [],
		urls: [],
		hasCurrencyMismatch: false,
		statistics: { min: 25, max: 40, average: 32, current: 29.99 },
		alerts: [],
		...overrides
	};
}

describe('ProductEditModal', () => {
	const defaultProps = {
		isOpen: true,
		product: createProduct(),
		onClose: vi.fn(),
		onSave: vi.fn().mockResolvedValue(undefined)
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(ProductEditModal, { props: defaultProps });
			expect(screen.getByText('Edit Product')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(ProductEditModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Edit Product')).not.toBeInTheDocument();
		});

		it('should fill the status toggle with the brand colour when active', async () => {
			render(ProductEditModal, { props: defaultProps });
			const toggle = screen.getByLabelText('Toggle product status');
			expect(toggle).toHaveClass('bg-brand');

			await fireEvent.click(toggle);
			expect(toggle).not.toHaveClass('bg-brand');
		});

		it('should render check interval select', () => {
			render(ProductEditModal, { props: defaultProps });
			expect(screen.getByLabelText('Check Interval')).toBeInTheDocument();
		});

		it('should show Default as selected when product has no interval', () => {
			render(ProductEditModal, { props: defaultProps });
			const select = screen.getByLabelText('Check Interval') as HTMLSelectElement;
			expect(select.value).toBe('');
		});

		it('should show current interval when product has one set', () => {
			render(ProductEditModal, {
				props: {
					...defaultProps,
					product: createProduct({ checkIntervalMinutes: 120 })
				}
			});
			const select = screen.getByLabelText('Check Interval') as HTMLSelectElement;
			expect(select.value).toBe('120');
		});

		it('should render all interval options', () => {
			render(ProductEditModal, { props: defaultProps });
			expect(screen.getByText('Default')).toBeInTheDocument();
			expect(screen.getByText('15 minutes')).toBeInTheDocument();
			expect(screen.getByText('1 hour')).toBeInTheDocument();
			expect(screen.getByText('24 hours')).toBeInTheDocument();
		});

		it('should show helper text for check interval', () => {
			render(ProductEditModal, { props: defaultProps });
			expect(
				screen.getByText('How often to check this product for price changes')
			).toBeInTheDocument();
		});
	});

	describe('check interval changes', () => {
		it('should include checkIntervalMinutes when interval is changed', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(ProductEditModal, {
				props: { ...defaultProps, onSave }
			});

			const select = screen.getByLabelText('Check Interval') as HTMLSelectElement;
			await fireEvent.change(select, { target: { value: '120' } });
			await fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

			expect(onSave).toHaveBeenCalledWith(
				expect.objectContaining({ checkIntervalMinutes: 120 })
			);
		});

		it('should send 0 when resetting to default', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(ProductEditModal, {
				props: {
					...defaultProps,
					product: createProduct({ checkIntervalMinutes: 120 }),
					onSave
				}
			});

			const select = screen.getByLabelText('Check Interval') as HTMLSelectElement;
			await fireEvent.change(select, { target: { value: '' } });
			await fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

			expect(onSave).toHaveBeenCalledWith(
				expect.objectContaining({ checkIntervalMinutes: 0 })
			);
		});

		it('should not include checkIntervalMinutes when unchanged', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(ProductEditModal, {
				props: {
					...defaultProps,
					product: createProduct({ name: 'Old Name' }),
					onSave
				}
			});

			// Only change name, leave interval as default
			const nameInput = screen.getByLabelText('Name') as HTMLInputElement;
			await fireEvent.input(nameInput, { target: { value: 'New Name' } });
			await fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

			expect(onSave).toHaveBeenCalledWith({ name: 'New Name' });
			expect(onSave.mock.calls[0][0]).not.toHaveProperty('checkIntervalMinutes');
		});
	});

	describe('form state while open', () => {
		it('should keep unsaved edits when the product prop changes while open', async () => {
			const product = createProduct();
			const { rerender } = render(ProductEditModal, { props: { ...defaultProps, product } });
			const nameInput = screen.getByLabelText('Name') as HTMLInputElement;
			await fireEvent.input(nameInput, { target: { value: 'Edited Name' } });

			await rerender({ product: { ...product, currentPrice: 9 } });

			expect(nameInput.value).toBe('Edited Name');
		});

		it('should reload the form from the product when reopened', async () => {
			const product = createProduct();
			const { rerender } = render(ProductEditModal, { props: { ...defaultProps, product } });
			await fireEvent.input(screen.getByLabelText('Name'), { target: { value: 'Discarded' } });

			await rerender({ isOpen: false });
			await rerender({ isOpen: true, product: { ...product, name: 'Renamed Elsewhere' } });

			expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Renamed Elsewhere');
		});
	});
});
