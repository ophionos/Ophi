import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AlertModal from './AlertModal.svelte';

describe('AlertModal', () => {
	const defaultProps = {
		isOpen: true,
		productName: 'Test Product',
		currentPrice: 100,
		currency: 'USD',
		onClose: vi.fn(),
		onSave: vi.fn()
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(AlertModal, { props: defaultProps });
			expect(screen.getByText('Create Price Alert')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(AlertModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Create Price Alert')).not.toBeInTheDocument();
		});

		it('should render AlertForm with correct props', () => {
			render(AlertModal, { props: defaultProps });
			expect(screen.getByText('Test Product')).toBeInTheDocument();
			expect(screen.getByText(/\$100\.00/)).toBeInTheDocument();
		});

		it('should render close button', () => {
			const { container } = render(AlertModal, { props: defaultProps });
			// The close button contains the X icon from lucide-svelte
			const closeButtons = container.querySelectorAll('button');
			expect(closeButtons.length).toBeGreaterThan(0);
		});
	});

	describe('closing modal', () => {
		it('should call onClose when close button is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(AlertModal, { props: { ...defaultProps, onClose } });

			// Find the header close button (first button with X icon)
			const headerButtons = container.querySelectorAll('.border-b button');
			const closeButton = headerButtons[0];
			await fireEvent.click(closeButton);

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when backdrop is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(AlertModal, { props: { ...defaultProps, onClose } });

			const backdrop = container.querySelector('.bg-black\\/50');
			await fireEvent.click(backdrop!);

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when Escape key is pressed', async () => {
			const onClose = vi.fn();
			render(AlertModal, { props: { ...defaultProps, onClose } });

			await fireEvent.keyDown(window, { key: 'Escape' });

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when Cancel button in form is clicked', async () => {
			const onClose = vi.fn();
			render(AlertModal, { props: { ...defaultProps, onClose } });

			await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

			expect(onClose).toHaveBeenCalled();
		});
	});

	describe('form submission', () => {
		it('should call onSave with target price and condition on form submit', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(AlertModal, { props: { ...defaultProps, onSave } });

			const input = screen.getByLabelText(/Target Price/);
			await fireEvent.input(input, { target: { value: '75' } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSave).toHaveBeenCalledWith(75, 'below');
			});
		});
	});

	describe('without current price', () => {
		it('should render without current price', () => {
			render(AlertModal, { props: { ...defaultProps, currentPrice: undefined } });
			expect(screen.getByText('Create Price Alert')).toBeInTheDocument();
			expect(screen.queryByText(/Current price:/)).not.toBeInTheDocument();
		});
	});

	describe('accessibility', () => {
		it('should have aria-labelledby pointing to the title', () => {
			render(AlertModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			expect(dialog).toHaveAttribute('aria-labelledby', 'alert-modal-title');
			expect(document.getElementById('alert-modal-title')).toHaveTextContent('Create Price Alert');
		});

		it('should have aria-modal on the dialog', () => {
			render(AlertModal, { props: defaultProps });
			expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true');
		});

		it('should have aria-label on the close button', () => {
			render(AlertModal, { props: defaultProps });
			expect(screen.getByLabelText('Close')).toBeInTheDocument();
		});

		it('should have touch-friendly close button (min 40px)', () => {
			render(AlertModal, { props: defaultProps });
			const closeBtn = screen.getByLabelText('Close');
			expect(closeBtn).toHaveClass('min-w-10');
			expect(closeBtn).toHaveClass('min-h-10');
		});

		it('should have overscroll-contain on scrollable body', () => {
			render(AlertModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			const scrollArea = dialog.querySelector('.overflow-y-auto');
			expect(scrollArea).toHaveClass('overscroll-contain');
		});
	});
});
