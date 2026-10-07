import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ResetPasswordPage from '../../../routes/auth/reset-password/+page.svelte';
import { api, ApiError } from '$lib/api/client';
import { page } from '$app/state';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			resetPassword: vi.fn()
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

function setToken(token: string) {
	page.url = new URL(`http://localhost/auth/reset-password?token=${token}`) as typeof page.url;
}

function clearToken() {
	page.url = new URL('http://localhost/auth/reset-password') as typeof page.url;
}

describe('Reset Password Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		setToken('test-token-123');
	});

	describe('rendering', () => {
		it('should render reset password form', () => {
			render(ResetPasswordPage);
			expect(screen.getByLabelText(/New password/i)).toBeInTheDocument();
			expect(screen.getByLabelText(/Confirm password/i)).toBeInTheDocument();
			expect(screen.getByRole('button', { name: /Reset password/i })).toBeInTheDocument();
		});

		it('should render page heading', () => {
			render(ResetPasswordPage);
			expect(screen.getByText('Ophi')).toBeInTheDocument();
			expect(screen.getByRole('heading', { name: /Set new password/i })).toBeInTheDocument();
		});

		it('should render link back to sign in', () => {
			render(ResetPasswordPage);
			expect(screen.getByRole('link', { name: /Sign in/i })).toHaveAttribute(
				'href',
				'/auth/login'
			);
		});
	});

	describe('form submission', () => {
		it('should call api.resetPassword with token and password', async () => {
			vi.mocked(api.resetPassword).mockResolvedValue({ message: 'ok' });

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(api.resetPassword).toHaveBeenCalledWith('test-token-123', 'NewPassword1');
			});
		});

		it('should show success toast after reset', async () => {
			vi.mocked(api.resetPassword).mockResolvedValue({ message: 'ok' });

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(toast.success).toHaveBeenCalledWith('Password reset successfully!');
			});
		});

		it('should show success message after reset', async () => {
			vi.mocked(api.resetPassword).mockResolvedValue({ message: 'ok' });

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByTestId('success-message')).toBeInTheDocument();
				expect(
					screen.getByText(/Your password has been reset successfully/i)
				).toBeInTheDocument();
			});
		});

		it('should show sign in link after success', async () => {
			vi.mocked(api.resetPassword).mockResolvedValue({ message: 'ok' });

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(
					screen.getByRole('link', { name: /Sign in with your new password/i })
				).toHaveAttribute('href', '/auth/login');
			});
		});
	});

	describe('password matching', () => {
		it('should show error when passwords do not match', async () => {
			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'DifferentPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Passwords do not match')).toBeInTheDocument();
			});

			expect(api.resetPassword).not.toHaveBeenCalled();
		});
	});

	describe('missing token', () => {
		it('should show error when token is missing', async () => {
			clearToken();

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(
					screen.getByText(/Missing reset token/i)
				).toBeInTheDocument();
			});

			expect(api.resetPassword).not.toHaveBeenCalled();
		});
	});

	describe('loading state', () => {
		it('should disable submit button during submission', async () => {
			let resolveReset: (value: { message: string }) => void;
			const resetPromise = new Promise<{ message: string }>((resolve) => {
				resolveReset = resolve;
			});
			vi.mocked(api.resetPassword).mockReturnValue(resetPromise);

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const submitButton = screen.getByRole('button', { name: /Reset password/i });
			const form = submitButton.closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			fireEvent.submit(form);

			await waitFor(() => {
				expect(submitButton).toBeDisabled();
			});

			resolveReset!({ message: 'ok' });
		});
	});

	describe('error handling', () => {
		it('should display error message on failure', async () => {
			vi.mocked(api.resetPassword).mockRejectedValue(
				new Error('Invalid or expired reset token')
			);

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'NewPassword1' } });
			await fireEvent.input(confirmInput, { target: { value: 'NewPassword1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(
					screen.getByText('Invalid or expired reset token')
				).toBeInTheDocument();
			});
		});

		it('should display field errors from API', async () => {
			const apiError = new ApiError('VALIDATION_ERROR', 'Validation failed', {
				NewPassword: ['Password must contain at least one uppercase letter']
			});
			vi.mocked(api.resetPassword).mockRejectedValue(apiError);

			render(ResetPasswordPage);

			const passwordInput = screen.getByLabelText(/New password/i);
			const confirmInput = screen.getByLabelText(/Confirm password/i);
			const form = screen.getByRole('button', { name: /Reset password/i }).closest('form')!;

			await fireEvent.input(passwordInput, { target: { value: 'password1' } });
			await fireEvent.input(confirmInput, { target: { value: 'password1' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(
					screen.getByText('Password must contain at least one uppercase letter')
				).toBeInTheDocument();
			});
		});
	});
});
