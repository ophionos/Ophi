import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ConfirmEmailPage from '../../../routes/auth/confirm-email/+page.svelte';
import { api } from '$lib/api/client';
import { page } from '$app/state';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			confirmEmailChange: vi.fn()
		}
	};
});

function setUrl(query: string) {
	page.url = new URL(`http://localhost/auth/confirm-email${query}`) as typeof page.url;
}

describe('Confirm Email Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		setUrl('?token=abc123');
	});

	it('should not confirm on load', () => {
		// Mail link scanners open links (some run scripts); only a click may use the token.
		render(ConfirmEmailPage);

		expect(screen.getByRole('button', { name: /Confirm email change/i })).toBeInTheDocument();
		expect(api.confirmEmailChange).not.toHaveBeenCalled();
	});

	it('should confirm with the token from the link when clicked', async () => {
		vi.mocked(api.confirmEmailChange).mockResolvedValue({ email: 'new@example.com' });
		render(ConfirmEmailPage);

		await fireEvent.click(screen.getByRole('button', { name: /Confirm email change/i }));

		await waitFor(() => {
			expect(api.confirmEmailChange).toHaveBeenCalledWith('abc123');
		});
	});

	it('should show the new email and a sign-in link when confirmed', async () => {
		vi.mocked(api.confirmEmailChange).mockResolvedValue({ email: 'new@example.com' });
		render(ConfirmEmailPage);

		await fireEvent.click(screen.getByRole('button', { name: /Confirm email change/i }));

		await waitFor(() => {
			expect(screen.getByTestId('success-message')).toHaveTextContent('new@example.com');
		});
		expect(screen.getByRole('link', { name: /Continue to Ophi/i })).toHaveAttribute('href', '/dashboard');
		expect(screen.queryByRole('button', { name: /Confirm email change/i })).not.toBeInTheDocument();
	});

	it('should show the error when the link is invalid or expired', async () => {
		vi.mocked(api.confirmEmailChange).mockRejectedValue(new Error('Invalid or expired confirmation link'));
		render(ConfirmEmailPage);

		await fireEvent.click(screen.getByRole('button', { name: /Confirm email change/i }));

		await waitFor(() => {
			expect(screen.getByText('Invalid or expired confirmation link')).toBeInTheDocument();
		});
	});

	it('should explain a missing token and offer no button when the link has no token', () => {
		setUrl('');
		render(ConfirmEmailPage);

		expect(screen.getByText(/link is incomplete/i)).toBeInTheDocument();
		expect(screen.queryByRole('button', { name: /Confirm email change/i })).not.toBeInTheDocument();
	});
});
