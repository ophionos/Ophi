import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import AddUrlModal from './AddUrlModal.svelte';

describe('AddUrlModal', () => {
	const defaultProps = {
		isOpen: true,
		onClose: vi.fn(),
		onSave: vi.fn()
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(AddUrlModal, { props: defaultProps });
			expect(screen.getByText('Add Store URL')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(AddUrlModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Add Store URL')).not.toBeInTheDocument();
		});

		it('should render URL input field', () => {
			render(AddUrlModal, { props: defaultProps });
			expect(screen.getByLabelText('Product URL')).toBeInTheDocument();
		});

		it('should render Add URL button', () => {
			render(AddUrlModal, { props: defaultProps });
			expect(screen.getByText('Add URL')).toBeInTheDocument();
		});

		it('should render Cancel button', () => {
			render(AddUrlModal, { props: defaultProps });
			expect(screen.getByText('Cancel')).toBeInTheDocument();
		});
	});

	describe('interactions', () => {
		it('should call onClose when Cancel is clicked', async () => {
			const onClose = vi.fn();
			render(AddUrlModal, { props: { ...defaultProps, onClose } });

			await fireEvent.click(screen.getByText('Cancel'));
			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when backdrop is clicked', async () => {
			const onClose = vi.fn();
			render(AddUrlModal, { props: { ...defaultProps, onClose } });

			const backdrop = screen.getByRole('presentation');
			await fireEvent.click(backdrop);
			expect(onClose).toHaveBeenCalled();
		});

		it('should have Add URL button disabled when input is empty', () => {
			render(AddUrlModal, { props: defaultProps });
			const button = screen.getByText('Add URL');
			expect(button).toBeDisabled();
		});
	});

	describe('validation', () => {
		it('should announce the error when the URL is invalid on blur', async () => {
			render(AddUrlModal, { props: defaultProps });
			const input = screen.getByLabelText('Product URL');
			await fireEvent.input(input, { target: { value: 'not-a-url' } });
			await fireEvent.blur(input);

			expect(screen.getByRole('alert')).toHaveTextContent('Please enter a valid URL');
		});
	});
});
