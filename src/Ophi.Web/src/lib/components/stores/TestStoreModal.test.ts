import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import TestStoreModal from './TestStoreModal.svelte';
import type { Store } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual<typeof import('$lib/api/client')>('$lib/api/client');
	return {
		...actual,
		api: {
			testStore: vi.fn()
		}
	};
});

describe('TestStoreModal', () => {
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
		store: mockStore,
		onClose: vi.fn()
	};

	describe('visibility', () => {
		it('should render modal when isOpen is true', () => {
			render(TestStoreModal, { props: defaultProps });
			expect(screen.getByRole('dialog')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(TestStoreModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
		});
	});

	describe('header', () => {
		it('should display store name in title', () => {
			render(TestStoreModal, { props: defaultProps });
			expect(screen.getByText('Test Test Store')).toBeInTheDocument();
		});
	});

	describe('URL input', () => {
		it('should render URL input field', () => {
			render(TestStoreModal, { props: defaultProps });
			expect(screen.getByLabelText('Test URL')).toBeInTheDocument();
		});

		it('should render test button', () => {
			render(TestStoreModal, { props: defaultProps });
			expect(screen.getByRole('button', { name: /test/i })).toBeInTheDocument();
		});

		it('should disable test button when URL is empty', () => {
			render(TestStoreModal, { props: defaultProps });
			const testButton = screen.getByRole('button', { name: /test/i });
			expect(testButton).toBeDisabled();
		});
	});

	describe('close mechanisms', () => {
		it('should call onClose when close button is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(TestStoreModal, { props: { ...defaultProps, onClose } });
			// The X button is the last button in the header
			const closeButtons = container.querySelectorAll('button');
			// Find the close button (the one without "Test" text)
			for (const btn of closeButtons) {
				if (!btn.textContent?.includes('Test')) {
					await fireEvent.click(btn);
					break;
				}
			}
			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when backdrop is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(TestStoreModal, { props: { ...defaultProps, onClose } });
			const backdrop = container.querySelector('[role="presentation"]');
			await fireEvent.click(backdrop!);
			expect(onClose).toHaveBeenCalled();
		});
	});
});
