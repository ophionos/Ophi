import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import LoginPage from '../../../routes/auth/login/+page.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { api, ApiError } from '$lib/api/client';
import { goto } from '$app/navigation';
import { toast } from '$lib/stores/toast.svelte';

// Mock the API client
vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			login: vi.fn()
		}
	};
});

// Mock goto
vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

vi.mock('$lib/stores/toast.svelte', () => ({
	toast: {
		success: vi.fn(),
		error: vi.fn(),
		info: vi.fn(),
		dismiss: vi.fn(),
		items: []
	}
}));

describe('Login Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		auth.logout();
	});

	describe('rendering', () => {
		it('should render login form', () => {
			render(LoginPage);
			expect(screen.getByLabelText(/Email address/i)).toBeInTheDocument();
			expect(screen.getByLabelText('Password')).toBeInTheDocument();
			expect(screen.getByRole('button', { name: /Sign in/i })).toBeInTheDocument();
		});

		it('should render Ophi branding', () => {
			render(LoginPage);
			expect(screen.getByText('Ophi')).toBeInTheDocument();
			expect(screen.getByText(/Sign in to your account/i)).toBeInTheDocument();
		});

		it('should render link to register page', () => {
			render(LoginPage);
			expect(screen.getByText(/Don't have an account/i)).toBeInTheDocument();
			expect(screen.getByRole('link', { name: /Sign up/i })).toHaveAttribute(
				'href',
				'/auth/register'
			);
		});

		it('should hide the sign-up link when sign-up is closed', () => {
			render(LoginPage, {
				props: { data: { user: null, authChecked: true, isPublic: true, registrationOpen: false } }
			});
			expect(screen.queryByText(/Don't have an account/i)).not.toBeInTheDocument();
			expect(screen.queryByRole('link', { name: /Sign up/i })).not.toBeInTheDocument();
		});

		it('should render theme toggle', () => {
			render(LoginPage);
			expect(
				screen.getByRole('button', { name: /Switch to dark mode|Switch to light mode/i })
			).toBeInTheDocument();
		});
	});

	describe('form validation', () => {
		it('should have required email field', () => {
			render(LoginPage);
			const emailInput = screen.getByLabelText(/Email address/i);
			expect(emailInput).toHaveAttribute('required');
		});

		it('should have required password field', () => {
			render(LoginPage);
			const passwordInput = screen.getByLabelText('Password');
			expect(passwordInput).toHaveAttribute('required');
		});

		it('should have email type on email input', () => {
			render(LoginPage);
			const emailInput = screen.getByLabelText(/Email address/i);
			expect(emailInput).toHaveAttribute('type', 'email');
		});

		it('should have password type on password input', () => {
			render(LoginPage);
			const passwordInput = screen.getByLabelText('Password');
			expect(passwordInput).toHaveAttribute('type', 'password');
		});

		it('should reveal and re-hide the password via the toggle', async () => {
			render(LoginPage);
			const passwordInput = screen.getByLabelText('Password');

			await fireEvent.click(screen.getByTestId('toggle-password'));
			expect(passwordInput).toHaveAttribute('type', 'text');

			await fireEvent.click(screen.getByTestId('toggle-password'));
			expect(passwordInput).toHaveAttribute('type', 'password');
		});

		it('should keep typed input when the password is revealed', async () => {
			render(LoginPage);
			const passwordInput = screen.getByLabelText('Password');

			await fireEvent.input(passwordInput, { target: { value: 'Secret123' } });
			await fireEvent.click(screen.getByTestId('toggle-password'));

			expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('Secret123');
		});
	});

	describe('form submission', () => {
		it('should call api.login with email and password', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.login).mockResolvedValue(mockUser);

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(api.login).toHaveBeenCalledWith('test@example.com', 'password123');
			});
		});

		it('should update auth store on successful login', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.login).mockResolvedValue(mockUser);

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(auth.current).toEqual(mockUser);
			});
		});

		it('should show success toast on login', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.login).mockResolvedValue(mockUser);

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(toast.success).toHaveBeenCalledWith('Welcome back!');
			});
		});

		it('should redirect to dashboard on successful login', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.login).mockResolvedValue(mockUser);

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(goto).toHaveBeenCalledWith('/dashboard');
			});
		});
	});

	describe('loading state', () => {
		it('should show loading spinner during submission', async () => {
			let resolveLogin: (value: { id: string; email: string; name: string }) => void;
			const loginPromise = new Promise((resolve) => {
				resolveLogin = resolve;
			});
			vi.mocked(api.login).mockReturnValue(
				loginPromise as Promise<{ id: string; email: string; name: string }>
			);

			const { container } = render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			fireEvent.submit(form);

			await waitFor(() => {
				const spinner = container.querySelector('.animate-spin');
				expect(spinner).toBeInTheDocument();
			});

			resolveLogin!({ id: '1', email: 'test@example.com', name: 'Test' });
		});

		it('should disable submit button during submission', async () => {
			let resolveLogin: (value: { id: string; email: string; name: string }) => void;
			const loginPromise = new Promise((resolve) => {
				resolveLogin = resolve;
			});
			vi.mocked(api.login).mockReturnValue(
				loginPromise as Promise<{ id: string; email: string; name: string }>
			);

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const submitButton = screen.getByRole('button', { name: /Sign in/i });
			const form = submitButton.closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			fireEvent.submit(form);

			await waitFor(() => {
				expect(submitButton).toBeDisabled();
			});

			resolveLogin!({ id: '1', email: 'test@example.com', name: 'Test' });
		});
	});

	describe('error handling', () => {
		it('should display error message on login failure', async () => {
			vi.mocked(api.login).mockRejectedValue(new Error('Invalid credentials'));

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'wrongpassword' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Invalid credentials')).toBeInTheDocument();
			});
		});

		it('should display field errors from API', async () => {
			const apiError = new ApiError('VALIDATION_ERROR', 'Validation failed', {
				email: ['Invalid email format']
			});
			vi.mocked(api.login).mockRejectedValue(apiError);

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'invalid' } });
			await fireEvent.input(passwordInput, { target: { value: 'password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Invalid email format')).toBeInTheDocument();
			});
		});

		it('should not redirect on login failure', async () => {
			vi.mocked(api.login).mockRejectedValue(new Error('Invalid credentials'));

			render(LoginPage);

			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText('Password');
			const form = screen.getByRole('button', { name: /Sign in/i }).closest('form')!;

			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'wrongpassword' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Invalid credentials')).toBeInTheDocument();
			});

			expect(goto).not.toHaveBeenCalled();
		});
	});
});
