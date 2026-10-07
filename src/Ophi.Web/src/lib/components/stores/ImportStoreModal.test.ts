import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ImportStoreModal from './ImportStoreModal.svelte';

describe('ImportStoreModal', () => {
	const defaultProps = {
		isOpen: true,
		onClose: vi.fn(),
		onImport: vi.fn()
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(ImportStoreModal, { props: defaultProps });
			expect(screen.getByText('Import Store Configuration')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(ImportStoreModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Import Store Configuration')).not.toBeInTheDocument();
		});

		it('should render file upload area', () => {
			render(ImportStoreModal, { props: defaultProps });
			expect(screen.getByText('Choose a .json file')).toBeInTheDocument();
		});

		it('should render import button', () => {
			render(ImportStoreModal, { props: defaultProps });
			expect(screen.getByText('Import')).toBeInTheDocument();
		});

		it('should render cancel button', () => {
			render(ImportStoreModal, { props: defaultProps });
			expect(screen.getByText('Cancel')).toBeInTheDocument();
		});
	});

	describe('JSON parsing', () => {
		it('should show error for invalid JSON', async () => {
			render(ImportStoreModal, { props: defaultProps });
			const textarea = screen.getByPlaceholderText(/storeId/);
			await fireEvent.input(textarea, { target: { value: '{invalid json' } });
			expect(screen.getByRole('alert')).toHaveTextContent('Invalid JSON format');
		});

		it('should show error for missing required fields', async () => {
			render(ImportStoreModal, { props: defaultProps });
			const textarea = screen.getByPlaceholderText(/storeId/);
			await fireEvent.input(textarea, { target: { value: '{"storeId": "test"}' } });
			expect(screen.getByText(/Missing required fields/)).toBeInTheDocument();
		});

		it('should show preview for valid JSON', async () => {
			render(ImportStoreModal, { props: defaultProps });
			const textarea = screen.getByPlaceholderText(/storeId/);
			const validJson = JSON.stringify({
				storeId: 'test-store',
				name: 'Test Store',
				domainPatterns: ['test.com'],
				selectors: {
					priceSelectors: ['.price'],
					nameSelectors: ['.name'],
					imageSelectors: ['.img']
				}
			});
			await fireEvent.input(textarea, { target: { value: validJson } });
			expect(screen.getByText('Preview')).toBeInTheDocument();
			expect(screen.getByText(/test-store/)).toBeInTheDocument();
			expect(screen.getByText(/Test Store/)).toBeInTheDocument();
		});
	});

	describe('import flow', () => {
		it('should disable import button when no valid data', () => {
			render(ImportStoreModal, { props: defaultProps });
			const importButton = screen.getByText('Import').closest('button');
			expect(importButton).toBeDisabled();
		});

		it('should enable import button when valid data is parsed', async () => {
			render(ImportStoreModal, { props: defaultProps });
			const textarea = screen.getByPlaceholderText(/storeId/);
			const validJson = JSON.stringify({
				storeId: 'test-store',
				name: 'Test Store',
				domainPatterns: ['test.com'],
				selectors: {
					priceSelectors: ['.price'],
					nameSelectors: ['.name'],
					imageSelectors: ['.img']
				}
			});
			await fireEvent.input(textarea, { target: { value: validJson } });
			const importButton = screen.getByText('Import').closest('button');
			expect(importButton).not.toBeDisabled();
		});

		it('should call onImport with parsed data when import is clicked', async () => {
			const onImport = vi.fn().mockResolvedValue(undefined);
			render(ImportStoreModal, { props: { ...defaultProps, onImport } });
			const textarea = screen.getByPlaceholderText(/storeId/);
			const validJson = JSON.stringify({
				storeId: 'test-store',
				name: 'Test Store',
				domainPatterns: ['test.com'],
				selectors: {
					priceSelectors: ['.price'],
					nameSelectors: ['.name'],
					imageSelectors: ['.img']
				}
			});
			await fireEvent.input(textarea, { target: { value: validJson } });
			const importButton = screen.getByText('Import').closest('button')!;
			await fireEvent.click(importButton);
			expect(onImport).toHaveBeenCalledWith({
				storeId: 'test-store',
				name: 'Test Store',
				domainPatterns: ['test.com'],
				selectors: {
					priceSelectors: ['.price'],
					nameSelectors: ['.name'],
					imageSelectors: ['.img']
				},
				priceLocale: undefined,
				requiresJavaScript: undefined
			});
		});

		it('should call onClose when cancel is clicked', async () => {
			const onClose = vi.fn();
			render(ImportStoreModal, { props: { ...defaultProps, onClose } });
			await fireEvent.click(screen.getByText('Cancel'));
			expect(onClose).toHaveBeenCalled();
		});
	});
});
