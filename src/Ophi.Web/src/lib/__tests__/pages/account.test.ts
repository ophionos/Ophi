import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AccountSettingsPage from '../../../routes/settings/account/+page.svelte';
import { auth } from '$lib/stores/auth.svelte';
import { api } from '$lib/api/client';
import { goto } from '$app/navigation';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			changePassword: vi.fn(),
			updateProfile: vi.fn(),
			deleteAccount: vi.fn(),
			// DisplayCurrencySetting loads these on mount.
			getSettings: vi.fn().mockResolvedValue({ displayCurrency: null }),
			getFxRates: vi.fn().mockResolvedValue({ base: 'EUR', asOf: null, rates: {}, supported: ['EUR', 'USD'] })
		}
	};
});

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

vi.mock('$lib/stores/liveUpdates.svelte', () => ({
	liveUpdates: {
		connect: vi.fn(),
		disconnect: vi.fn(),
		subscribe: vi.fn()
	}
}));

const user = { id: 'u1', email: 'me@example.com', name: 'Original Name' };

function renderPage() {
	return render(AccountSettingsPage, {
		props: { data: { user } as never }
	});
}

beforeEach(() => {
	vi.clearAllMocks();
	auth.logout();
});

describe('Account Settings — Profile card', () => {
	it('should hydrate name and email from the loader user', () => {
		renderPage();

		expect(screen.getByTestId('profile-name')).toHaveValue('Original Name');
		expect(screen.getByTestId('profile-email')).toHaveValue('me@example.com');
	});

	it('should not show the current-password field when the email is unchanged', () => {
		renderPage();

		expect(screen.queryByTestId('profile-current-password')).not.toBeInTheDocument();
	});

	it('should show the current-password field when the email is edited', async () => {
		renderPage();

		await fireEvent.input(screen.getByTestId('profile-email'), {
			target: { value: 'new@example.com' }
		});

		expect(screen.getByTestId('profile-current-password')).toBeInTheDocument();
	});

	it('should save a name-only change without a password', async () => {
		vi.mocked(api.updateProfile).mockResolvedValue({
			id: 'u1',
			email: 'me@example.com',
			name: 'New Name'
		});
		renderPage();

		await fireEvent.input(screen.getByTestId('profile-name'), {
			target: { value: 'New Name' }
		});
		await fireEvent.click(screen.getByTestId('profile-save'));

		await waitFor(() => {
			expect(api.updateProfile).toHaveBeenCalledWith({
				name: 'New Name',
				email: 'me@example.com'
			});
		});
		expect(toast.success).toHaveBeenCalled();
	});

	it('should include the current password when saving an email change', async () => {
		vi.mocked(api.updateProfile).mockResolvedValue({
			id: 'u1',
			email: 'new@example.com',
			name: 'Original Name'
		});
		renderPage();

		await fireEvent.input(screen.getByTestId('profile-email'), {
			target: { value: 'new@example.com' }
		});
		await fireEvent.input(screen.getByTestId('profile-current-password'), {
			target: { value: 'Password1' }
		});
		await fireEvent.click(screen.getByTestId('profile-save'));

		await waitFor(() => {
			expect(api.updateProfile).toHaveBeenCalledWith({
				name: 'Original Name',
				email: 'new@example.com',
				currentPassword: 'Password1'
			});
		});
	});

	it('should update the auth store after a successful save', async () => {
		vi.mocked(api.updateProfile).mockResolvedValue({
			id: 'u1',
			email: 'me@example.com',
			name: 'New Name'
		});
		renderPage();

		await fireEvent.input(screen.getByTestId('profile-name'), {
			target: { value: 'New Name' }
		});
		await fireEvent.click(screen.getByTestId('profile-save'));

		await waitFor(() => {
			expect(auth.current?.name).toBe('New Name');
		});
	});

	it('should show an error toast when the save fails', async () => {
		vi.mocked(api.updateProfile).mockRejectedValue(new Error('This email is already in use'));
		renderPage();

		await fireEvent.input(screen.getByTestId('profile-email'), {
			target: { value: 'taken@example.com' }
		});
		await fireEvent.input(screen.getByTestId('profile-current-password'), {
			target: { value: 'Password1' }
		});
		await fireEvent.click(screen.getByTestId('profile-save'));

		await waitFor(() => {
			expect(toast.error).toHaveBeenCalledWith('This email is already in use');
		});
	});

	it('should disable save when nothing changed', () => {
		renderPage();

		expect(screen.getByTestId('profile-save')).toBeDisabled();
	});
});

describe('Account Settings — Password card', () => {
	it('should change the password and clear the fields on success', async () => {
		vi.mocked(api.changePassword).mockResolvedValue({ message: 'Password changed.' });
		renderPage();

		await fireEvent.input(screen.getByTestId('password-current'), {
			target: { value: 'OldPassword1' }
		});
		await fireEvent.input(screen.getByTestId('password-new'), {
			target: { value: 'NewPassword1' }
		});
		await fireEvent.input(screen.getByTestId('password-confirm'), {
			target: { value: 'NewPassword1' }
		});
		await fireEvent.click(screen.getByTestId('password-save'));

		await waitFor(() => {
			expect(api.changePassword).toHaveBeenCalledWith('OldPassword1', 'NewPassword1');
		});
		expect(toast.success).toHaveBeenCalled();
		expect(screen.getByTestId('password-current')).toHaveValue('');
		expect(screen.getByTestId('password-new')).toHaveValue('');
	});

	it('should not call the API when the confirmation does not match', async () => {
		renderPage();

		await fireEvent.input(screen.getByTestId('password-current'), {
			target: { value: 'OldPassword1' }
		});
		await fireEvent.input(screen.getByTestId('password-new'), {
			target: { value: 'NewPassword1' }
		});
		await fireEvent.input(screen.getByTestId('password-confirm'), {
			target: { value: 'Different1' }
		});
		await fireEvent.click(screen.getByTestId('password-save'));

		expect(api.changePassword).not.toHaveBeenCalled();
		expect(screen.getByText(/do not match/i)).toBeInTheDocument();
	});

	it('should show an error toast when the current password is wrong', async () => {
		vi.mocked(api.changePassword).mockRejectedValue(new Error('Current password is incorrect'));
		renderPage();

		await fireEvent.input(screen.getByTestId('password-current'), {
			target: { value: 'WrongPassword1' }
		});
		await fireEvent.input(screen.getByTestId('password-new'), {
			target: { value: 'NewPassword1' }
		});
		await fireEvent.input(screen.getByTestId('password-confirm'), {
			target: { value: 'NewPassword1' }
		});
		await fireEvent.click(screen.getByTestId('password-save'));

		await waitFor(() => {
			expect(toast.error).toHaveBeenCalledWith('Current password is incorrect');
		});
	});
});

describe('Account Settings — Danger Zone', () => {
	it('should open the confirmation modal from the delete button', async () => {
		renderPage();

		await fireEvent.click(screen.getByTestId('delete-account-open'));

		expect(screen.getByTestId('delete-account-password')).toBeInTheDocument();
	});

	it('should disable confirm until a password is entered', async () => {
		renderPage();

		await fireEvent.click(screen.getByTestId('delete-account-open'));

		expect(screen.getByTestId('delete-account-confirm')).toBeDisabled();

		await fireEvent.input(screen.getByTestId('delete-account-password'), {
			target: { value: 'Password1' }
		});

		expect(screen.getByTestId('delete-account-confirm')).toBeEnabled();
	});

	it('should delete the account and redirect to login', async () => {
		vi.mocked(api.deleteAccount).mockResolvedValue(undefined as never);
		renderPage();

		await fireEvent.click(screen.getByTestId('delete-account-open'));
		await fireEvent.input(screen.getByTestId('delete-account-password'), {
			target: { value: 'Password1' }
		});
		await fireEvent.click(screen.getByTestId('delete-account-confirm'));

		await waitFor(() => {
			expect(api.deleteAccount).toHaveBeenCalledWith('Password1');
		});
		await waitFor(() => {
			expect(goto).toHaveBeenCalledWith('/auth/login');
		});
	});

	it('should show an error toast and stay when the password is wrong', async () => {
		vi.mocked(api.deleteAccount).mockRejectedValue(new Error('Password is incorrect'));
		renderPage();

		await fireEvent.click(screen.getByTestId('delete-account-open'));
		await fireEvent.input(screen.getByTestId('delete-account-password'), {
			target: { value: 'WrongPassword1' }
		});
		await fireEvent.click(screen.getByTestId('delete-account-confirm'));

		await waitFor(() => {
			expect(toast.error).toHaveBeenCalledWith('Password is incorrect');
		});
		expect(goto).not.toHaveBeenCalled();
	});
});
