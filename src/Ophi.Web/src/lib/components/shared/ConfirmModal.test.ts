import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import ConfirmModal from './ConfirmModal.svelte';

describe('ConfirmModal', () => {
	const defaultProps = {
		isOpen: true,
		title: 'Delete Item',
		message: 'Are you sure you want to delete this item?',
		onConfirm: vi.fn(),
		onCancel: vi.fn()
	};

	it('should render when isOpen is true', () => {
		render(ConfirmModal, { props: defaultProps });

		expect(screen.getByText('Delete Item')).toBeInTheDocument();
		expect(screen.getByText('Are you sure you want to delete this item?')).toBeInTheDocument();
	});

	it('should not render when isOpen is false', () => {
		render(ConfirmModal, { props: { ...defaultProps, isOpen: false } });

		expect(screen.queryByText('Delete Item')).not.toBeInTheDocument();
	});

	it('should render default button text', () => {
		render(ConfirmModal, { props: defaultProps });

		expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
		expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
	});

	it('should render custom button text', () => {
		render(ConfirmModal, {
			props: {
				...defaultProps,
				confirmText: 'Yes, Remove',
				cancelText: 'No, Keep'
			}
		});

		expect(screen.getByRole('button', { name: 'Yes, Remove' })).toBeInTheDocument();
		expect(screen.getByRole('button', { name: 'No, Keep' })).toBeInTheDocument();
	});

	it('should call onConfirm when confirm button is clicked', async () => {
		const onConfirm = vi.fn();
		render(ConfirmModal, { props: { ...defaultProps, onConfirm } });

		await fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

		expect(onConfirm).toHaveBeenCalled();
	});

	it('should call onCancel when cancel button is clicked', async () => {
		const onCancel = vi.fn();
		render(ConfirmModal, { props: { ...defaultProps, onCancel } });

		await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

		expect(onCancel).toHaveBeenCalled();
	});

	it('should call onCancel when backdrop is clicked', async () => {
		const onCancel = vi.fn();
		render(ConfirmModal, { props: { ...defaultProps, onCancel } });

		const backdrop = screen.getByRole('presentation');
		await fireEvent.click(backdrop);

		expect(onCancel).toHaveBeenCalled();
	});

	it('should not call onCancel when backdrop is clicked during loading', async () => {
		const onCancel = vi.fn();
		render(ConfirmModal, { props: { ...defaultProps, onCancel, loading: true } });

		const backdrop = screen.getByRole('presentation');
		await fireEvent.click(backdrop);

		expect(onCancel).not.toHaveBeenCalled();
	});

	it('should disable buttons when loading', () => {
		render(ConfirmModal, { props: { ...defaultProps, loading: true } });

		expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
		expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
	});

	it('should show loading spinner when loading', () => {
		const { container } = render(ConfirmModal, { props: { ...defaultProps, loading: true } });

		// Look for the animate-spin class on any element
		const spinner = container.querySelector('.animate-spin');
		expect(spinner).toBeInTheDocument();
	});

	it('should render alert icon', () => {
		const { container } = render(ConfirmModal, { props: defaultProps });

		// Look for the alert triangle icon (SVG element within the warning icon container)
		const iconContainer = container.querySelector('.bg-red-100, .dark\\:bg-red-900');
		expect(iconContainer).toBeInTheDocument();
		const svg = iconContainer?.querySelector('svg');
		expect(svg).toBeInTheDocument();
	});

	describe('accessibility', () => {
		it('should have aria-labelledby pointing to the title', () => {
			render(ConfirmModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			const labelledBy = dialog.getAttribute('aria-labelledby');
			expect(labelledBy).toBe('confirm-modal-title');
			const title = document.getElementById('confirm-modal-title');
			expect(title).toHaveTextContent('Delete Item');
		});

		it('should have aria-describedby pointing to the message', () => {
			render(ConfirmModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			const describedBy = dialog.getAttribute('aria-describedby');
			expect(describedBy).toBe('confirm-modal-desc');
			const desc = document.getElementById('confirm-modal-desc');
			expect(desc).toHaveTextContent('Are you sure you want to delete this item?');
		});

		it('should have aria-modal on the dialog', () => {
			render(ConfirmModal, { props: defaultProps });
			const dialog = screen.getByRole('dialog');
			expect(dialog).toHaveAttribute('aria-modal', 'true');
		});

		it('should have touch-friendly buttons (min-h-10)', () => {
			render(ConfirmModal, { props: defaultProps });
			const buttons = screen.getAllByRole('button');
			buttons.forEach((btn) => {
				expect(btn).toHaveClass('min-h-10');
			});
		});
	});
});
