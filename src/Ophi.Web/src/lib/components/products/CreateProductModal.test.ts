import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import CreateProductModal from './CreateProductModal.svelte';

describe('CreateProductModal', () => {
	const defaultProps = {
		isOpen: true,
		onClose: vi.fn(),
		onSave: vi.fn()
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByText('Create Product from Scratch')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(CreateProductModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Create Product from Scratch')).not.toBeInTheDocument();
		});

		it('should render name input field', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByLabelText(/Product Name/)).toBeInTheDocument();
		});

		it('should render image URL input field', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByLabelText(/Image URL/)).toBeInTheDocument();
		});

		it('should render currency select', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByLabelText('Currency')).toBeInTheDocument();
		});

		it('should render Create Product button', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByText('Create Product')).toBeInTheDocument();
		});

		it('should render Cancel button', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByText('Cancel')).toBeInTheDocument();
		});

		it('should render helper text about adding URLs later', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(
				screen.getByText(/You can add store URLs from the product detail page/)
			).toBeInTheDocument();
		});
	});

	describe('validation', () => {
		it('should show error when name is empty on submit', async () => {
			render(CreateProductModal, { props: defaultProps });

			await fireEvent.click(screen.getByTestId('create-product-submit'));
			expect(screen.getByRole('alert')).toHaveTextContent('Name is required');
		});

		it('should link the name input to its error when validation fails', async () => {
			render(CreateProductModal, { props: defaultProps });

			await fireEvent.click(screen.getByTestId('create-product-submit'));
			const input = screen.getByTestId('create-product-name');
			expect(input).toHaveAttribute('aria-invalid', 'true');
			expect(input).toHaveAccessibleDescription('Name is required');
		});

		it('should show error for invalid image URL', async () => {
			render(CreateProductModal, { props: defaultProps });

			const nameInput = screen.getByTestId('create-product-name');
			const imageInput = screen.getByTestId('create-product-image-url');

			await fireEvent.input(nameInput, { target: { value: 'My Product' } });
			await fireEvent.input(imageInput, { target: { value: 'not-a-url' } });
			await fireEvent.click(screen.getByTestId('create-product-submit'));

			expect(screen.getByTestId('image-url-error')).toBeInTheDocument();
		});

		it('should not show error for valid image URL', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(CreateProductModal, { props: { ...defaultProps, onSave } });

			const nameInput = screen.getByTestId('create-product-name');
			const imageInput = screen.getByTestId('create-product-image-url');

			await fireEvent.input(nameInput, { target: { value: 'My Product' } });
			await fireEvent.input(imageInput, {
				target: { value: 'https://example.com/image.jpg' }
			});
			await fireEvent.click(screen.getByTestId('create-product-submit'));

			expect(screen.queryByTestId('image-url-error')).not.toBeInTheDocument();
		});
	});

	describe('interactions', () => {
		it('should call onClose when Cancel is clicked', async () => {
			const onClose = vi.fn();
			render(CreateProductModal, { props: { ...defaultProps, onClose } });

			await fireEvent.click(screen.getByText('Cancel'));
			expect(onClose).toHaveBeenCalled();
		});

		it('should call onSave with form data on valid submit', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(CreateProductModal, { props: { ...defaultProps, onSave } });

			const nameInput = screen.getByTestId('create-product-name');
			await fireEvent.input(nameInput, { target: { value: 'My Product' } });
			await fireEvent.click(screen.getByTestId('create-product-submit'));

			expect(onSave).toHaveBeenCalledWith({
				name: 'My Product',
				imageUrl: undefined,
				currency: 'USD'
			});
		});

		it('should call onSave with image URL when provided', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(CreateProductModal, { props: { ...defaultProps, onSave } });

			await fireEvent.input(screen.getByTestId('create-product-name'), {
				target: { value: 'My Product' }
			});
			await fireEvent.input(screen.getByTestId('create-product-image-url'), {
				target: { value: 'https://example.com/img.jpg' }
			});
			await fireEvent.click(screen.getByTestId('create-product-submit'));

			expect(onSave).toHaveBeenCalledWith({
				name: 'My Product',
				imageUrl: 'https://example.com/img.jpg',
				currency: 'USD'
			});
		});

		it('should show error message when onSave throws', async () => {
			const onSave = vi.fn().mockRejectedValue(new Error('Server error'));
			render(CreateProductModal, { props: { ...defaultProps, onSave } });

			await fireEvent.input(screen.getByTestId('create-product-name'), {
				target: { value: 'My Product' }
			});
			await fireEvent.click(screen.getByTestId('create-product-submit'));

			expect(await screen.findByTestId('create-product-error')).toHaveTextContent(
				'Server error'
			);
		});

		it('should not call onSave when name is empty', async () => {
			const onSave = vi.fn();
			render(CreateProductModal, { props: { ...defaultProps, onSave } });

			await fireEvent.click(screen.getByTestId('create-product-submit'));
			expect(onSave).not.toHaveBeenCalled();
		});
	});

	describe('accessibility', () => {
		it('should have aria-labelledby pointing to the title', () => {
			render(CreateProductModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			expect(dialog).toHaveAttribute('aria-labelledby', 'create-product-modal-title');
			expect(document.getElementById('create-product-modal-title')).toHaveTextContent(
				'Create Product from Scratch'
			);
		});

		it('should have aria-modal on the dialog', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true');
		});

		it('should have aria-label on the close button', () => {
			render(CreateProductModal, { props: defaultProps });
			expect(screen.getByLabelText('Close')).toBeInTheDocument();
		});

		it('should have touch-friendly close button (min 40px)', () => {
			render(CreateProductModal, { props: defaultProps });
			const closeBtn = screen.getByLabelText('Close');
			expect(closeBtn).toHaveClass('min-w-10');
			expect(closeBtn).toHaveClass('min-h-10');
		});

		it('should have overscroll-contain on scrollable body', () => {
			render(CreateProductModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			const scrollArea = dialog.querySelector('.overflow-y-auto');
			expect(scrollArea).toHaveClass('overscroll-contain');
		});
	});
});
