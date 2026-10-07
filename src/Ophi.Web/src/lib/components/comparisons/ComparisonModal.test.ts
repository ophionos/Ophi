import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ComparisonModal from './ComparisonModal.svelte';
import type { ComparisonGroupDetail } from '$lib/api/client';

describe('ComparisonModal', () => {
	const mockGroup: ComparisonGroupDetail = {
		id: 'group-1',
		name: 'Test Group',
		description: 'Test description',
		products: [],
		updatedAt: '2024-01-15T10:00:00Z'
	};

	const defaultProps = {
		isOpen: true,
		onClose: vi.fn(),
		onSave: vi.fn()
	};

	describe('rendering', () => {
		it('should render modal when isOpen is true', () => {
			render(ComparisonModal, { props: defaultProps });
			expect(screen.getByText('Create Comparison Group')).toBeInTheDocument();
		});

		it('should not render modal when isOpen is false', () => {
			render(ComparisonModal, { props: { ...defaultProps, isOpen: false } });
			expect(screen.queryByText('Create Comparison Group')).not.toBeInTheDocument();
		});

		it('should show "Create Comparison Group" title when no group provided', () => {
			render(ComparisonModal, { props: defaultProps });
			expect(screen.getByText('Create Comparison Group')).toBeInTheDocument();
		});

		it('should show "Edit Comparison Group" title when group is provided', () => {
			render(ComparisonModal, { props: { ...defaultProps, group: mockGroup } });
			expect(screen.getByText('Edit Comparison Group')).toBeInTheDocument();
		});

		it('should render ComparisonForm', () => {
			render(ComparisonModal, { props: defaultProps });
			expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
			expect(screen.getByLabelText(/Description/)).toBeInTheDocument();
		});
	});

	describe('closing modal', () => {
		it('should call onClose when backdrop is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(ComparisonModal, { props: { ...defaultProps, onClose } });

			const backdrop = container.querySelector('.bg-black\\/50');
			await fireEvent.click(backdrop!);

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when Escape key is pressed', async () => {
			const onClose = vi.fn();
			render(ComparisonModal, { props: { ...defaultProps, onClose } });

			await fireEvent.keyDown(window, { key: 'Escape' });

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when close button in header is clicked', async () => {
			const onClose = vi.fn();
			const { container } = render(ComparisonModal, { props: { ...defaultProps, onClose } });

			const headerButtons = container.querySelectorAll('.border-b button');
			await fireEvent.click(headerButtons[0]);

			expect(onClose).toHaveBeenCalled();
		});

		it('should call onClose when Cancel button in form is clicked', async () => {
			const onClose = vi.fn();
			render(ComparisonModal, { props: { ...defaultProps, onClose } });

			await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

			expect(onClose).toHaveBeenCalled();
		});
	});

	describe('form submission', () => {
		it('should call onSave with name and description on form submit', async () => {
			const onSave = vi.fn().mockResolvedValue(undefined);
			render(ComparisonModal, { props: { ...defaultProps, onSave } });

			const nameInput = screen.getByLabelText(/Name/);
			const descInput = screen.getByLabelText(/Description/);
			await fireEvent.input(nameInput, { target: { value: 'My Group' } });
			await fireEvent.input(descInput, { target: { value: 'My description' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSave).toHaveBeenCalledWith('My Group', 'My description');
			});
		});
	});

	describe('edit mode', () => {
		it('should pre-populate form with group values', () => {
			render(ComparisonModal, { props: { ...defaultProps, group: mockGroup } });
			const nameInput = screen.getByLabelText(/Name/) as HTMLInputElement;
			const descInput = screen.getByLabelText(/Description/) as HTMLTextAreaElement;
			expect(nameInput.value).toBe('Test Group');
			expect(descInput.value).toBe('Test description');
		});

		it('should show Update button in edit mode', () => {
			render(ComparisonModal, { props: { ...defaultProps, group: mockGroup } });
			expect(screen.getByRole('button', { name: 'Update' })).toBeInTheDocument();
		});
	});
});
