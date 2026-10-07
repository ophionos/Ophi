import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import StoreModal from './StoreModal.svelte';
import type { Store } from '$lib/api/client';

describe('StoreModal', () => {
	const mockStore: Store = {
		id: 'store-1',
		storeId: 'test-store',
		name: 'Test Store',
		domainPatterns: ['example.com'],
		selectors: {
			priceSelectors: ['.price'],
			nameSelectors: ['.name'],
			imageSelectors: ['.image']
		},
		isBuiltIn: false,
		priceLocale: 'en-US',
		requiresJavaScript: false
	};

	const defaultProps = {
		isOpen: true,
		onClose: vi.fn(),
		onSave: vi.fn()
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(StoreModal, { props: defaultProps });
			expect(screen.getByText('Create Store Configuration')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(StoreModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Create Store Configuration')).not.toBeInTheDocument();
		});

		it('should show "Create Store Configuration" title when no store provided', () => {
			render(StoreModal, { props: defaultProps });
			expect(screen.getByText('Create Store Configuration')).toBeInTheDocument();
		});

		it('should show "Edit Store Configuration" title when store is provided', () => {
			render(StoreModal, { props: { ...defaultProps, store: mockStore } });
			expect(screen.getByText('Edit Store Configuration')).toBeInTheDocument();
		});

		it('should render StoreForm', () => {
			render(StoreModal, { props: defaultProps });
			// StoreForm renders the Display Name label
			expect(screen.getByLabelText(/Display Name/)).toBeInTheDocument();
		});
	});

	describe('closing modal', () => {
		it('should call onClose when backdrop is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(StoreModal, { props: { ...defaultProps, onClose } });

			const backdrop = container.querySelector('.bg-black\\/50');
			await fireEvent.click(backdrop!);

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when Escape key is pressed', async () => {
			const onClose = vi.fn();
			render(StoreModal, { props: { ...defaultProps, onClose } });

			await fireEvent.keyDown(window, { key: 'Escape' });

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when close button in header is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(StoreModal, { props: { ...defaultProps, onClose } });

			const headerButtons = container.querySelectorAll('.border-b button');
			await fireEvent.click(headerButtons[0]);

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when Cancel button in form is clicked', async () => {
			const onClose = vi.fn();
			render(StoreModal, { props: { ...defaultProps, onClose } });

			await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

			expect(onClose).toHaveBeenCalled();
		});
	});

	describe('edit mode', () => {
		it('should not show Store ID field in edit mode', () => {
			render(StoreModal, { props: { ...defaultProps, store: mockStore } });
			expect(screen.queryByLabelText(/Store ID/)).not.toBeInTheDocument();
		});

		it('should show Store ID field in create mode', () => {
			render(StoreModal, { props: defaultProps });
			expect(screen.getByLabelText(/Store ID/)).toBeInTheDocument();
		});

		it('should pre-populate form with store values in edit mode', () => {
			render(StoreModal, { props: { ...defaultProps, store: mockStore } });
			const nameInput = screen.getByLabelText(/Display Name/) as HTMLInputElement;
			expect(nameInput.value).toBe('Test Store');
		});
	});
});
