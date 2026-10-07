import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ComparisonForm from './ComparisonForm.svelte';
import { ApiError } from '$lib/api/client';
import type { ComparisonGroupDetail } from '$lib/api/client';

describe('ComparisonForm', () => {
	const defaultProps = {
		onSubmit: vi.fn(),
		onCancel: vi.fn()
	};

	const mockGroup: ComparisonGroupDetail = {
		id: 'group-1',
		name: 'Test Group',
		description: 'Test description',
		products: [],
		updatedAt: '2024-01-15T10:00:00Z'
	};

	describe('rendering', () => {
		it('should render name input', () => {
			render(ComparisonForm, { props: defaultProps });
			expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
		});

		it('should render description textarea', () => {
			render(ComparisonForm, { props: defaultProps });
			expect(screen.getByLabelText(/Description/)).toBeInTheDocument();
		});

		it('should render cancel and submit buttons', () => {
			render(ComparisonForm, { props: defaultProps });
			expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
			expect(screen.getByRole('button', { name: 'Create' })).toBeInTheDocument();
		});

		it('should show "Create" button text in create mode', () => {
			render(ComparisonForm, { props: defaultProps });
			expect(screen.getByRole('button', { name: 'Create' })).toBeInTheDocument();
		});

		it('should show "Update" button text in edit mode', () => {
			render(ComparisonForm, { props: { ...defaultProps, group: mockGroup } });
			expect(screen.getByRole('button', { name: 'Update' })).toBeInTheDocument();
		});

		it('should pre-fill form with group data in edit mode', () => {
			render(ComparisonForm, { props: { ...defaultProps, group: mockGroup } });
			const nameInput = screen.getByLabelText(/Name/) as HTMLInputElement;
			const descInput = screen.getByLabelText(/Description/) as HTMLTextAreaElement;
			expect(nameInput.value).toBe('Test Group');
			expect(descInput.value).toBe('Test description');
		});

		it('should have empty inputs in create mode', () => {
			render(ComparisonForm, { props: defaultProps });
			const nameInput = screen.getByLabelText(/Name/) as HTMLInputElement;
			const descInput = screen.getByLabelText(/Description/) as HTMLTextAreaElement;
			expect(nameInput.value).toBe('');
			expect(descInput.value).toBe('');
		});
	});

	describe('validation', () => {
		it('should show error when name is empty', async () => {
			const onSubmit = vi.fn();
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			expect(screen.getByRole('alert')).toHaveTextContent('Name is required');
			expect(onSubmit).not.toHaveBeenCalled();
		});

		it('should show error when name is only whitespace', async () => {
			const onSubmit = vi.fn();
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: '   ' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			expect(screen.getByText('Name is required')).toBeInTheDocument();
			expect(onSubmit).not.toHaveBeenCalled();
		});
	});

	describe('form submission', () => {
		it('should call onSubmit with name and description', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			const descInput = screen.getByLabelText(/Description/);
			await fireEvent.input(nameInput, { target: { value: 'My Group' } });
			await fireEvent.input(descInput, { target: { value: 'My description' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith('My Group', 'My description');
			});
		});

		it('should call onSubmit with undefined description when empty', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: 'My Group' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith('My Group', undefined);
			});
		});

		it('should trim name and description', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			const descInput = screen.getByLabelText(/Description/);
			await fireEvent.input(nameInput, { target: { value: '  My Group  ' } });
			await fireEvent.input(descInput, { target: { value: '  My description  ' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith('My Group', 'My description');
			});
		});
	});

	describe('loading state', () => {
		it('should show loading spinner during submission', async () => {
			let resolveSubmit: () => void;
			const submitPromise = new Promise<void>((resolve) => {
				resolveSubmit = resolve;
			});
			const onSubmit = vi.fn().mockReturnValue(submitPromise);

			const { container } = render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: 'Test' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
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

			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: 'Test' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByLabelText(/Name/)).toBeDisabled();
				expect(screen.getByLabelText(/Description/)).toBeDisabled();
				expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
			});

			resolveSubmit!();
		});
	});

	describe('error handling', () => {
		it('should display error message on submission failure', async () => {
			const onSubmit = vi.fn().mockRejectedValue(new Error('Network error'));
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: 'Test' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Network error')).toBeInTheDocument();
			});
		});

		it('should display API error message', async () => {
			const onSubmit = vi
				.fn()
				.mockRejectedValue(new ApiError('VALIDATION_ERROR', 'Group name already exists'));
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: 'Test' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Group name already exists')).toBeInTheDocument();
			});
		});

		it('should display generic error for non-Error exceptions', async () => {
			const onSubmit = vi.fn().mockRejectedValue('Something went wrong');
			render(ComparisonForm, { props: { ...defaultProps, onSubmit } });

			const nameInput = screen.getByLabelText(/Name/);
			await fireEvent.input(nameInput, { target: { value: 'Test' } });

			const form = screen.getByRole('button', { name: 'Create' }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('An error occurred')).toBeInTheDocument();
			});
		});
	});

	describe('cancel button', () => {
		it('should call onCancel when cancel button is clicked', async () => {
			const onCancel = vi.fn();
			render(ComparisonForm, { props: { ...defaultProps, onCancel } });

			await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

			expect(onCancel).toHaveBeenCalled();
		});
	});
});
