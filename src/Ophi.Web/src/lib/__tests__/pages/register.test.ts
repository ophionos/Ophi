import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import RegisterPage from '../../../routes/auth/register/+page.svelte';
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
			register: vi.fn()
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

describe('Register Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		auth.logout();
	});

	describe('rendering', () => {
		it('should render registration form', () => {
			render(RegisterPage);
			expect(screen.getByLabelText(/^Name$/i)).toBeInTheDocument();
			expect(screen.getByLabelText(/Email address/i)).toBeInTheDocument();
			expect(screen.getByLabelText(/^Password$/i)).toBeInTheDocument();
			expect(screen.getByLabelText(/Confirm Password/i)).toBeInTheDocument();
			expect(screen.getByRole('button', { name: /Create account/i })).toBeInTheDocument();
		});

		it('should render Ophi branding', () => {
			render(RegisterPage);
			expect(screen.getByText('Ophi')).toBeInTheDocument();
			expect(screen.getByText(/Create your account/i)).toBeInTheDocument();
		});

		it('should render link to login page', () => {
			render(RegisterPage);
			expect(screen.getByText(/Already have an account/i)).toBeInTheDocument();
			expect(screen.getByRole('link', { name: /Sign in/i })).toHaveAttribute('href', '/auth/login');
		});

		it('should render theme toggle', () => {
			render(RegisterPage);
			expect(
				screen.getByRole('button', { name: /Switch to dark mode|Switch to light mode/i })
			).toBeInTheDocument();
		});

		it('should display password requirements', () => {
			render(RegisterPage);
			expect(screen.getByText(/At least 8 characters/i)).toBeInTheDocument();
		});

		it('should show a closed notice instead of the form when sign-up is closed', () => {
			render(RegisterPage, {
				props: { data: { user: null, authChecked: true, isPublic: true, registrationOpen: false } }
			});
			expect(screen.getByText(/Sign-up is closed/i)).toBeInTheDocument();
			expect(screen.queryByLabelText(/Email address/i)).not.toBeInTheDocument();
			expect(screen.queryByRole('button', { name: /Create account/i })).not.toBeInTheDocument();
			expect(screen.getByRole('link', { name: /Sign in/i })).toHaveAttribute('href', '/auth/login');
		});
	});

	describe('form validation', () => {
		it('should have required name field', () => {
			render(RegisterPage);
			const nameInput = screen.getByLabelText(/^Name$/i);
			expect(nameInput).toHaveAttribute('required');
		});

		it('should have required email field', () => {
			render(RegisterPage);
			const emailInput = screen.getByLabelText(/Email address/i);
			expect(emailInput).toHaveAttribute('required');
		});

		it('should have required password field with minlength', () => {
			render(RegisterPage);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			expect(passwordInput).toHaveAttribute('required');
			expect(passwordInput).toHaveAttribute('minlength', '8');
		});

		it('should have required confirm password field', () => {
			render(RegisterPage);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			expect(confirmInput).toHaveAttribute('required');
		});
	});

	describe('password confirmation', () => {
		it('should show error when passwords do not match', async () => {
			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'DifferentPassword' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText(/Passwords do not match/i)).toBeInTheDocument();
			});

			expect(api.register).not.toHaveBeenCalled();
		});
	});

	describe('form submission', () => {
		it('should call api.register with form data', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.register).mockResolvedValue(mockUser);

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(api.register).toHaveBeenCalledWith('test@example.com', 'Password123', 'Test User');
			});
		});

		it('should update auth store on successful registration', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.register).mockResolvedValue(mockUser);

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(auth.current).toEqual(mockUser);
			});
		});

		it('should show success toast on registration', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.register).mockResolvedValue(mockUser);

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(toast.success).toHaveBeenCalledWith('Account created!');
			});
		});

		it('should redirect to dashboard on successful registration', async () => {
			const mockUser = { id: '1', email: 'test@example.com', name: 'Test User' };
			vi.mocked(api.register).mockResolvedValue(mockUser);

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(goto).toHaveBeenCalledWith('/dashboard');
			});
		});
	});

	describe('loading state', () => {
		it('should show loading spinner during submission', async () => {
			let resolveRegister: (value: { id: string; email: string; name: string }) => void;
			const registerPromise = new Promise((resolve) => {
				resolveRegister = resolve;
			});
			vi.mocked(api.register).mockReturnValue(
				registerPromise as Promise<{ id: string; email: string; name: string }>
			);

			const { container } = render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			fireEvent.submit(form);

			await waitFor(() => {
				const spinner = container.querySelector('.animate-spin');
				expect(spinner).toBeInTheDocument();
			});

			resolveRegister!({ id: '1', email: 'test@example.com', name: 'Test User' });
		});

		it('should disable submit button during submission', async () => {
			let resolveRegister: (value: { id: string; email: string; name: string }) => void;
			const registerPromise = new Promise((resolve) => {
				resolveRegister = resolve;
			});
			vi.mocked(api.register).mockReturnValue(
				registerPromise as Promise<{ id: string; email: string; name: string }>
			);

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const submitButton = screen.getByRole('button', { name: /Create account/i });
			const form = submitButton.closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			fireEvent.submit(form);

			await waitFor(() => {
				expect(submitButton).toBeDisabled();
			});

			resolveRegister!({ id: '1', email: 'test@example.com', name: 'Test User' });
		});
	});

	describe('error handling', () => {
		it('should display error message on registration failure', async () => {
			vi.mocked(api.register).mockRejectedValue(new Error('Email already in use'));

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'existing@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Email already in use')).toBeInTheDocument();
			});
		});

		it('should display field errors from API', async () => {
			const apiError = new ApiError('VALIDATION_ERROR', 'Validation failed', {
				password: ['Password must contain a special character']
			});
			vi.mocked(api.register).mockRejectedValue(apiError);

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'weakpass' } });
			await fireEvent.input(confirmInput, { target: { value: 'weakpass' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Password must contain a special character')).toBeInTheDocument();
			});
		});

		it('should not redirect on registration failure', async () => {
			vi.mocked(api.register).mockRejectedValue(new Error('Registration failed'));

			render(RegisterPage);

			const nameInput = screen.getByLabelText(/^Name$/i);
			const emailInput = screen.getByLabelText(/Email address/i);
			const passwordInput = screen.getByLabelText(/^Password$/i);
			const confirmInput = screen.getByLabelText(/Confirm Password/i);
			const form = screen.getByRole('button', { name: /Create account/i }).closest('form')!;

			await fireEvent.input(nameInput, { target: { value: 'Test User' } });
			await fireEvent.input(emailInput, { target: { value: 'test@example.com' } });
			await fireEvent.input(passwordInput, { target: { value: 'Password123' } });
			await fireEvent.input(confirmInput, { target: { value: 'Password123' } });
			await fireEvent.submit(form);

			await waitFor(() => {
				expect(screen.getByText('Registration failed')).toBeInTheDocument();
			});

			expect(goto).not.toHaveBeenCalled();
		});
	});
});
