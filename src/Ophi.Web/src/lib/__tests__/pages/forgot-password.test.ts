import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ForgotPasswordPage from '../../../routes/auth/forgot-password/+page.svelte';
import { api, ApiError } from '$lib/api/client';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			forgotPassword: vi.fn()
		}
	};
});

vi.mock('$lib/stores/toast.svelte', () => ({
	toast: {
		success: vi.fn(),
		error: vi.fn(),
		info: vi.fn(),
		dismiss: vi.fn(),
		items: []
	}
}));

describe('Forgot Password Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('rendering', () => {
		it('should render forgot password form', () => {
			render(ForgotPasswordPage);
			expect(screen.getByLabelText(/Email address/i)).toBeInTheDocument();
			expect(screen.getByRole('button', { name: /Send reset link/i })).toBeInTheDocument();
		});

		it('should render page heading', () => {
			render(ForgotPasswordPage);
			expect(screen.getByText('Ophi')).toBeInTheDocument();
			expect(
				screen.getByRole('heading', { name: /Reset your password/i })
			).toBeInTheDocument();
		});

		it('should render description text', () => {
			render(ForgotPasswordPage);
			expect(
				screen.getByText(/Enter your email address and we'll send you a link/i)
			).toBeInTheDocument();
		});

		it('should render link back to sign in', () => {
			render(ForgotPasswordPage);
			expect(screen.getByRole('link', { name: /Sign in/i })).toHaveAttribute(
				'href',
				'/auth/login'
			);
		});
	});

	describe('form submission', () => {
		it('should call api.forgotPassword with email', async () => {
			vi.mocked(api.forgotPassword).mockResolvedValue({ message: 'ok' });

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(api.forgotPassword).toHaveBeenCalledWith('test@example.com');
			});
		});

		it('should show success toast after submission', async () => {
			vi.mocked(api.forgotPassword).mockResolvedValue({ message: 'ok' });

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(toast.success).toHaveBeenCalledWith('Reset link sent!');
			});
		});

		it('should show success message after submission', async () => {
			vi.mocked(api.forgotPassword).mockResolvedValue({ message: 'ok' });

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByTestId('success-message')).toBeInTheDocument();
				expect(
					screen.getByText(/a password reset link has been sent/i)
				).toBeInTheDocument();
			});
		});

		it('should hide form after successful submission', async () => {
			vi.mocked(api.forgotPassword).mockResolvedValue({ message: 'ok' });

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.queryByLabelText(/Email address/i)).not.toBeInTheDocument();
				expect(
					screen.queryByRole('button', { name: /Send reset link/i })
				).not.toBeInTheDocument();
			});
		});

		it('should show "Back to sign in" link after success', async () => {
			vi.mocked(api.forgotPassword).mockResolvedValue({ message: 'ok' });

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByRole('link', { name: /Back to sign in/i })).toHaveAttribute(
					'href',
					'/auth/login'
				);
			});
		});
	});

	describe('loading state', () => {
		it('should disable submit button during submission', async () => {
			let resolveForgot: (value: { message: string }) => void;
			const forgotPromise = new Promise<{ message: string }>((resolve) => {
				resolveForgot = resolve;
			});
			vi.mocked(api.forgotPassword).mockReturnValue(forgotPromise);

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const submitButton = screen.getByRole('button', { name: /Send reset link/i });
			const form = submitButton.closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			fireEvent.submit(form);

			await waitFor(() => {
				expect(submitButton).toBeDisabled();
			});

			resolveForgot!({ message: 'ok' });
		});

		it('should show loading spinner during submission', async () => {
			let resolveForgot: (value: { message: string }) => void;
			const forgotPromise = new Promise<{ message: string }>((resolve) => {
				resolveForgot = resolve;
			});
			vi.mocked(api.forgotPassword).mockReturnValue(forgotPromise);

			const { container } = render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			fireEvent.submit(form);

			await waitFor(() => {
				expect(container.querySelector('.animate-spin')).toBeInTheDocument();
			});

			resolveForgot!({ message: 'ok' });
		});
	});

	describe('error handling', () => {
		it('should display error message on failure', async () => {
			vi.mocked(api.forgotPassword).mockRejectedValue(new Error('Network error'));

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Network error')).toBeInTheDocument();
			});
		});

		it('should display field errors from API', async () => {
			const apiError = new ApiError('VALIDATION_ERROR', 'Validation failed', {
				email: ['Email is required']
			});
			vi.mocked(api.forgotPassword).mockRejectedValue(apiError);

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: '' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Email is required')).toBeInTheDocument();
			});
		});

		it('should not show success message on error', async () => {
			vi.mocked(api.forgotPassword).mockRejectedValue(new Error('Network error'));

			render(ForgotPasswordPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const form = screen.getByRole('button', { name: /Send reset link/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Network error')).toBeInTheDocument();
			});

			expect(screen.queryByTestId('success-message')).not.toBeInTheDocument();
		});
	});
});
