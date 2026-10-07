import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AddProductForm from './AddProductForm.svelte';
import { ApiError } from '$lib/api/client';

describe('AddProductForm', () => {
	it('should render input and submit button', () => {
		const onSubmit = vi.fn();
		render(AddProductForm, { props: { onSubmit } });

		expect(screen.getByPlaceholderText('Paste product URL...')).toBeInTheDocument();
		expect(screen.getByRole('button', { name: /Add/i })).toBeInTheDocument();
	});

	it('should call onSubmit with URL when form is submitted', async () => {
		const onSubmit = vi.fn().mockResolvedValue(undefined);
		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		await waitFor(() => {
			expect(onSubmit).toHaveBeenCalledWith('https://example.com/product');
		});
	});

	it('should clear input after successful submission', async () => {
		const onSubmit = vi.fn().mockResolvedValue(undefined);
		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...') as HTMLInputElement;
		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		await waitFor(() => {
			expect(input.value).toBe('');
		});
	});

	it('should not submit when URL is empty', async () => {
		const onSubmit = vi.fn();
		render(AddProductForm, { props: { onSubmit } });

		await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		expect(onSubmit).not.toHaveBeenCalled();
	});

	it('should not submit when URL is only whitespace', async () => {
		const onSubmit = vi.fn();
		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		await fireEvent.input(input, { target: { value: '   ' } });
		await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		expect(onSubmit).not.toHaveBeenCalled();
	});

	it('should disable input and button while loading', async () => {
		// Create a promise that we can control
		let resolveSubmit: () => void;
		const submitPromise = new Promise<void>((resolve) => {
			resolveSubmit = resolve;
		});
		const onSubmit = vi.fn().mockReturnValue(submitPromise);

		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		const button = screen.getByRole('button', { name: /Add/i });

		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		fireEvent.submit(button.closest('form')!);

		// Wait for loading state
		await waitFor(() => {
			expect(input).toBeDisabled();
			expect(button).toBeDisabled();
		});

		// Resolve the promise to complete
		resolveSubmit!();
	});

	it('should show loading spinner while submitting', async () => {
		let resolveSubmit: () => void;
		const submitPromise = new Promise<void>((resolve) => {
			resolveSubmit = resolve;
		});
		const onSubmit = vi.fn().mockReturnValue(submitPromise);

		const { container } = render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		await waitFor(() => {
			const spinner = container.querySelector('.animate-spin');
			expect(spinner).toBeInTheDocument();
		});

		resolveSubmit!();
	});

	it('should display error message when submission fails', async () => {
		const onSubmit = vi.fn().mockRejectedValue(new Error('Invalid URL'));
		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		await waitFor(() => {
			expect(screen.getByRole('alert')).toHaveTextContent('Invalid URL');
		});
	});

	it('should display generic error message for non-Error exceptions', async () => {
		const onSubmit = vi.fn().mockRejectedValue('Something went wrong');
		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

		await waitFor(() => {
			expect(screen.getByText('Failed to add product')).toBeInTheDocument();
		});
	});

	it('should clear error message on new submission attempt', async () => {
		const onSubmit = vi
			.fn()
			.mockRejectedValueOnce(new Error('First error'))
			.mockResolvedValueOnce(undefined);

		render(AddProductForm, { props: { onSubmit } });

		const input = screen.getByPlaceholderText('Paste product URL...');
		const form = screen.getByRole('button', { name: /Add/i }).closest('form')!;

		// First submission - should fail
		await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
		await fireEvent.submit(form);

		await waitFor(() => {
			expect(screen.getByText('First error')).toBeInTheDocument();
		});

		// Second submission - error should be cleared
		await fireEvent.input(input, { target: { value: 'https://example.com/product2' } });
		fireEvent.submit(form);

		await waitFor(() => {
			expect(screen.queryByText('First error')).not.toBeInTheDocument();
		});
	});

	describe('Error Differentiation', () => {
		it('should show field-level error from ApiError with field details', async () => {
			const onSubmit = vi.fn().mockRejectedValue(
				new ApiError('VALIDATION_ERROR', 'Validation failed', { url: ['URL is not valid'] })
			);
			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(screen.getByText('URL is not valid')).toBeInTheDocument();
			});
			// Should NOT show retry button for field errors
			expect(screen.queryByLabelText('Retry adding product')).not.toBeInTheDocument();
		});

		it('should show retry button for network/server errors', async () => {
			const onSubmit = vi.fn().mockRejectedValue(
				new ApiError('UnknownError', 'An error occurred')
			);
			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(screen.getByText('Something went wrong. Please try again.')).toBeInTheDocument();
				expect(screen.getByLabelText('Retry adding product')).toBeInTheDocument();
			});
		});

		it('should show retry button for non-ApiError exceptions', async () => {
			const onSubmit = vi.fn().mockRejectedValue('Something went wrong');
			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(screen.getByLabelText('Retry adding product')).toBeInTheDocument();
			});
		});

		it('should show ApiError message without retry for known error codes', async () => {
			const onSubmit = vi.fn().mockRejectedValue(
				new ApiError('DUPLICATE_URL', 'Product already exists')
			);
			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(screen.getByText('Product already exists')).toBeInTheDocument();
			});
			expect(screen.queryByLabelText('Retry adding product')).not.toBeInTheDocument();
		});

		it('should retry submission when retry button is clicked', async () => {
			const onSubmit = vi.fn()
				.mockRejectedValueOnce(new ApiError('UnknownError', 'An error occurred'))
				.mockResolvedValueOnce(undefined);

			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(screen.getByLabelText('Retry adding product')).toBeInTheDocument();
			});

			await fireEvent.click(screen.getByLabelText('Retry adding product'));

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledTimes(2);
			});
		});
	});

	describe('Accessibility', () => {
		it('should set aria-invalid on input when error is present', async () => {
			const onSubmit = vi.fn().mockRejectedValue(new Error('Invalid URL'));
			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(input).toHaveAttribute('aria-invalid', 'true');
			});
		});

		it('should set aria-describedby linking to error message', async () => {
			const onSubmit = vi.fn().mockRejectedValue(new Error('Invalid URL'));
			render(AddProductForm, { props: { onSubmit } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			await fireEvent.input(input, { target: { value: 'https://example.com/product' } });
			await fireEvent.submit(screen.getByRole('button', { name: /Add/i }).closest('form')!);

			await waitFor(() => {
				expect(input).toHaveAttribute('aria-describedby', 'add-product-error');
				expect(document.getElementById('add-product-error')).toBeInTheDocument();
			});
		});

		it('should not set aria-invalid when no error', () => {
			render(AddProductForm, { props: { onSubmit: vi.fn() } });

			const input = screen.getByPlaceholderText('Paste product URL...');
			expect(input).not.toHaveAttribute('aria-invalid');
		});
	});
});
