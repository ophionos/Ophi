import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AlertForm from './AlertForm.svelte';
import { ApiError } from '$lib/api/client';

describe('AlertForm', () => {
	const defaultProps = {
		productName: 'Test Product',
		currentPrice: 100,
		currency: 'USD',
		onSubmit: vi.fn(),
		onCancel: vi.fn()
	};

	describe('rendering', () => {
		it('should render product name', () => {
			render(AlertForm, { props: defaultProps });
			expect(screen.getByText('Test Product')).toBeInTheDocument();
		});

		it('should render current price when provided', () => {
			render(AlertForm, { props: defaultProps });
			expect(screen.getByText(/\$100\.00/)).toBeInTheDocument();
		});

		it('should not render current price when not provided', () => {
			render(AlertForm, { props: { ...defaultProps, currentPrice: undefined } });
			expect(screen.queryByText(/Current price:/)).not.toBeInTheDocument();
		});

		it('should render all condition options', () => {
			render(AlertForm, { props: defaultProps });
			expect(screen.getByText('Price drops below')).toBeInTheDocument();
			expect(screen.getByText('Price goes above')).toBeInTheDocument();
			expect(screen.getByText('Price drops by percentage')).toBeInTheDocument();
		});

		it('should render target price input', () => {
			render(AlertForm, { props: defaultProps });
			expect(screen.getByLabelText(/Target Price/)).toBeInTheDocument();
		});

		it('should render cancel and submit buttons', () => {
			render(AlertForm, { props: defaultProps });
			expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
			expect(screen.getByRole('button', { name: /Create Alert/i })).toBeInTheDocument();
		});

		it('should default target price to 90% of current price', () => {
			render(AlertForm, { props: defaultProps });
			const input = screen.getByLabelText(/Target Price/) as HTMLInputElement;
			expect(input.value).toBe('90');
		});

		it('should default target price to 0 when no current price', () => {
			render(AlertForm, { props: { ...defaultProps, currentPrice: undefined } });
			const input = screen.getByLabelText(/Target Price/) as HTMLInputElement;
			expect(input.value).toBe('0');
		});
	});

	describe('condition selection', () => {
		it('should have "below" condition selected by default', () => {
			render(AlertForm, { props: defaultProps });
			const belowRadio = screen.getByDisplayValue('below') as HTMLInputElement;
			expect(belowRadio.checked).toBe(true);
		});

		it('should allow selecting different conditions', async () => {
			render(AlertForm, { props: defaultProps });

			const aboveRadio = screen.getByDisplayValue('above') as HTMLInputElement;
			await fireEvent.click(aboveRadio);

			expect(aboveRadio.checked).toBe(true);
		});

		it('should change label to percentage when percentDrop is selected', async () => {
			render(AlertForm, { props: defaultProps });

			const percentRadio = screen.getByDisplayValue('percentDrop');
			await fireEvent.click(percentRadio);

			expect(screen.getByText(/Drop Percentage/)).toBeInTheDocument();
		});
	});

	describe('form submission', () => {
		it('should call onSubmit with target price and condition', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const input = screen.getByLabelText(/Target Price/);
			await fireEvent.input(input, { target: { value: '75' } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith(75, 'below');
			});
		});

		it('should call onSubmit with selected condition', async () => {
			const onSubmit = vi.fn().mockResolvedValue(undefined);
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const aboveRadio = screen.getByDisplayValue('above');
			await fireEvent.click(aboveRadio);

			const input = screen.getByLabelText(/Target Price/);
			await fireEvent.input(input, { target: { value: '150' } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(onSubmit).toHaveBeenCalledWith(150, 'above');
			});
		});
	});

	describe('validation', () => {
		it('should not submit when target price is 0', async () => {
			const onSubmit = vi.fn();
			render(AlertForm, { props: { ...defaultProps, onSubmit, currentPrice: undefined } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			expect(onSubmit).not.toHaveBeenCalled();
			expect(screen.getByText('Target price must be greater than 0')).toBeInTheDocument();
		});

		it('should not submit when target price is negative', async () => {
			const onSubmit = vi.fn();
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const input = screen.getByLabelText(/Target Price/);
			await fireEvent.input(input, { target: { value: '-10' } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			expect(onSubmit).not.toHaveBeenCalled();
			expect(screen.getByText('Target price must be greater than 0')).toBeInTheDocument();
		});
	});

	describe('loading state', () => {
		it('should show loading spinner during submission', async () => {
			let resolveSubmit: () => void;
			const submitPromise = new Promise<void>((resolve) => {
				resolveSubmit = resolve;
			});
			const onSubmit = vi.fn().mockReturnValue(submitPromise);

			const { container } = render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
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

			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByLabelText(/Target Price/)).toBeDisabled();
				expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
				expect(screen.getByRole('button', { name: /Create Alert/i })).toBeDisabled();
			});

			resolveSubmit!();
		});

		it('should disable radio buttons during submission', async () => {
			let resolveSubmit: () => void;
			const submitPromise = new Promise<void>((resolve) => {
				resolveSubmit = resolve;
			});
			const onSubmit = vi.fn().mockReturnValue(submitPromise);

			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByDisplayValue('below')).toBeDisabled();
				expect(screen.getByDisplayValue('above')).toBeDisabled();
				expect(screen.getByDisplayValue('percentDrop')).toBeDisabled();
			});

			resolveSubmit!();
		});
	});

	describe('error handling', () => {
		it('should display error message on submission failure', async () => {
			const onSubmit = vi.fn().mockRejectedValue(new Error('Network error'));
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByRole('alert')).toHaveTextContent('Network error');
			});
		});

		it('should display API error message', async () => {
			const onSubmit = vi
				.fn()
				.mockRejectedValue(new ApiError('VALIDATION_ERROR', 'Invalid target price'));
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Invalid target price')).toBeInTheDocument();
			});
		});

		it('should display field errors from API', async () => {
			const apiError = new ApiError('VALIDATION_ERROR', 'Validation failed', {
				targetprice: ['Target price must be less than current price']
			});
			const onSubmit = vi.fn().mockRejectedValue(apiError);
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(
					screen.getByText('Target price must be less than current price')
				).toHaveAttribute('role', 'alert');
			});
		});

		it('should display generic error for non-Error exceptions', async () => {
			const onSubmit = vi.fn().mockRejectedValue('Something went wrong');
			render(AlertForm, { props: { ...defaultProps, onSubmit } });

			const form = screen.getByRole('button', { name: /Create Alert/i }).closest('form')!;
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('An error occurred')).toBeInTheDocument();
			});
		});
	});

	describe('cancel button', () => {
		it('should call onCancel when cancel button is clicked', async () => {
			const onCancel = vi.fn();
			render(AlertForm, { props: { ...defaultProps, onCancel } });

			await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

			expect(onCancel).toHaveBeenCalled();
		});
	});
});
