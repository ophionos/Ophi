<script lang="ts">
	import type { PageData } from './$types';
	import { TriangleAlert } from 'lucide-svelte';
	import { api } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import Modal from '$lib/components/shared/Modal.svelte';
	import DisplayCurrencySetting from '$lib/components/settings/DisplayCurrencySetting.svelte';
	import { liveUpdates } from '$lib/stores/liveUpdates.svelte';
	import { notifications } from '$lib/stores/notifications.svelte';
	import { products } from '$lib/stores/products.svelte';
	import { tags } from '$lib/stores/tags.svelte';
	import { comparisons } from '$lib/stores/comparisons.svelte';
	import { stores } from '$lib/stores/stores.svelte';

	let { data }: { data: PageData } = $props();

	// Profile card
	let savedName = $state('');
	let savedEmail = $state('');
	let name = $state('');
	let email = $state('');
	let profileCurrentPassword = $state('');
	let profileLoading = $state(false);
	// Known only from this page's last save: the loader user (cookie claims) does not carry it.
	let pendingEmail = $state<string | null>(null);

	$effect(() => {
		savedName = data.user?.name ?? '';
		savedEmail = data.user?.email ?? '';
		name = data.user?.name ?? '';
		email = data.user?.email ?? '';
	});

	const emailChanged = $derived(email.trim().toLowerCase() !== savedEmail.toLowerCase());
	const profileDirty = $derived(name.trim() !== savedName || emailChanged);
	const profileSaveDisabled = $derived(
		profileLoading ||
			!profileDirty ||
			!name.trim() ||
			!email.trim() ||
			(emailChanged && !profileCurrentPassword)
	);

	async function handleSaveProfile(e: SubmitEvent) {
		e.preventDefault();
		profileLoading = true;
		try {
			const payload: { name: string; email: string; currentPassword?: string } = {
				name: name.trim(),
				email: email.trim()
			};
			if (emailChanged) {
				payload.currentPassword = profileCurrentPassword;
			}
			const updated = await api.updateProfile(payload);
			savedName = updated.name;
			savedEmail = updated.email;
			name = updated.name;
			email = updated.email;
			profileCurrentPassword = '';
			pendingEmail = updated.pendingEmail ?? null;
			auth.setUser({ id: updated.id, email: updated.email, name: updated.name });
			// currentPassword is only sent with an email change, so a name-only save keeps the plain toast.
			if (payload.currentPassword && updated.pendingEmail) {
				toast.success(`Check ${updated.pendingEmail} to confirm the new email`);
			} else {
				toast.success('Profile updated');
			}
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update profile');
		} finally {
			profileLoading = false;
		}
	}

	// Password card
	let currentPassword = $state('');
	let newPassword = $state('');
	let confirmPassword = $state('');
	let passwordError = $state('');
	let passwordLoading = $state(false);

	const passwordSaveDisabled = $derived(
		passwordLoading || !currentPassword || !newPassword || !confirmPassword
	);

	async function handleChangePassword(e: SubmitEvent) {
		e.preventDefault();
		passwordError = '';
		if (newPassword !== confirmPassword) {
			passwordError = 'New passwords do not match';
			return;
		}
		passwordLoading = true;
		try {
			await api.changePassword(currentPassword, newPassword);
			currentPassword = '';
			newPassword = '';
			confirmPassword = '';
			toast.success('Password changed. Other sessions have been signed out.');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to change password');
		} finally {
			passwordLoading = false;
		}
	}

	// Danger zone
	let deleteModalOpen = $state(false);
	let deletePassword = $state('');
	let deleteLoading = $state(false);

	function closeDeleteModal() {
		if (deleteLoading) return;
		deleteModalOpen = false;
		deletePassword = '';
	}

	async function handleDeleteAccount() {
		deleteLoading = true;
		try {
			await api.deleteAccount(deletePassword);
			auth.logout();
			liveUpdates.disconnect();
			notifications.clear();
			products.clear();
			tags.clear();
			comparisons.clear();
			stores.clear();
			goto(resolve('/auth/login'));
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to delete account');
			deleteLoading = false;
		}
	}
</script>

<svelte:head>
	<title>Account Settings - Ophi</title>
</svelte:head>

<div class="space-y-3">
	<!-- Profile -->
	<form
		onsubmit={handleSaveProfile}
		class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3"
	>
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Profile</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Your display name and the email you sign in with
			</p>
		</div>

		<div class="grid sm:grid-cols-2 gap-3">
			<div>
				<label for="profile-name" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
					Name
				</label>
				<input
					id="profile-name"
					type="text"
					bind:value={name}
					required
					maxlength={100}
					disabled={profileLoading}
					class="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
					data-testid="profile-name"
				/>
			</div>
			<div>
				<label for="profile-email" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
					Email
				</label>
				<input
					id="profile-email"
					type="email"
					bind:value={email}
					required
					maxlength={256}
					disabled={profileLoading}
					class="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
					data-testid="profile-email"
				/>
			</div>
		</div>

		{#if pendingEmail}
			<p
				class="text-xs text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-md px-3 py-2"
				data-testid="profile-pending-email"
			>
				Waiting for confirmation from <strong>{pendingEmail}</strong>. You sign in with
				{savedEmail} until you open the link sent there.
			</p>
		{/if}

		{#if emailChanged}
			<div>
				<label for="profile-current-password" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
					Current password
				</label>
				<input
					id="profile-current-password"
					type="password"
					bind:value={profileCurrentPassword}
					autocomplete="current-password"
					disabled={profileLoading}
					class="w-full sm:max-w-xs px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
					data-testid="profile-current-password"
				/>
				<p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
					Changing your email requires your current password
				</p>
			</div>
		{/if}

		<div class="flex justify-end">
			<button
				type="submit"
				disabled={profileSaveDisabled}
				class="px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
				data-testid="profile-save"
			>
				{profileLoading ? 'Saving...' : 'Save changes'}
			</button>
		</div>
	</form>

	<!-- Password -->
	<form
		onsubmit={handleChangePassword}
		class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3"
	>
		<div>
			<p class="text-sm font-medium text-gray-900 dark:text-white">Password</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Changing your password signs out every other session
			</p>
		</div>

		<div class="grid sm:grid-cols-3 gap-3">
			<div>
				<label for="password-current" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
					Current password
				</label>
				<input
					id="password-current"
					type="password"
					bind:value={currentPassword}
					required
					autocomplete="current-password"
					disabled={passwordLoading}
					class="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
					data-testid="password-current"
				/>
			</div>
			<div>
				<label for="password-new" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
					New password
				</label>
				<input
					id="password-new"
					type="password"
					bind:value={newPassword}
					required
					autocomplete="new-password"
					disabled={passwordLoading}
					class="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
					data-testid="password-new"
				/>
			</div>
			<div>
				<label for="password-confirm" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
					Confirm new password
				</label>
				<input
					id="password-confirm"
					type="password"
					bind:value={confirmPassword}
					required
					autocomplete="new-password"
					disabled={passwordLoading}
					class="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
					data-testid="password-confirm"
				/>
			</div>
		</div>

		<div class="flex items-center justify-between gap-3">
			<p aria-live="polite" class="text-xs {passwordError ? 'text-red-600 dark:text-red-400' : 'text-gray-400 dark:text-gray-500'}">
				{passwordError || 'At least 8 characters with an uppercase letter, lowercase letter, and digit'}
			</p>
			<button
				type="submit"
				disabled={passwordSaveDisabled}
				class="shrink-0 px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
				data-testid="password-save"
			>
				{passwordLoading ? 'Changing...' : 'Change password'}
			</button>
		</div>
	</form>

	<!-- Danger zone -->
	<DisplayCurrencySetting />

	<div class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-red-300 dark:border-red-900 space-y-3">
		<div class="flex items-center justify-between gap-3">
			<div>
				<p class="text-sm font-medium text-red-600 dark:text-red-400">Delete account</p>
				<p class="text-xs text-gray-500 dark:text-gray-400">
					Permanently deletes your account and everything in it — products, price history, alerts,
					notifications, tags, comparisons, store configs, webhooks, and API keys
				</p>
			</div>
			<button
				type="button"
				onclick={() => (deleteModalOpen = true)}
				class="shrink-0 px-3 py-1.5 text-sm font-medium text-white bg-red-600 hover:bg-red-700 rounded-md transition-colors"
				data-testid="delete-account-open"
			>
				Delete account
			</button>
		</div>
	</div>
</div>

<Modal
	isOpen={deleteModalOpen}
	title="Delete account"
	size="md"
	closeDisabled={deleteLoading}
	onClose={closeDeleteModal}
>
	<div class="p-6 space-y-4">
		<div class="flex items-start gap-3">
			<div class="shrink-0 w-10 h-10 rounded-full bg-red-100 dark:bg-red-900 flex items-center justify-center">
				<TriangleAlert size={20} class="text-red-600 dark:text-red-400" />
			</div>
			<p class="text-sm text-gray-600 dark:text-gray-300">
				This permanently deletes your account and <strong>all</strong> of your data. There is no
				undo. Enter your password to confirm.
			</p>
		</div>

		<div>
			<label for="delete-account-password" class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
				Password
			</label>
			<input
				id="delete-account-password"
				type="password"
				bind:value={deletePassword}
				autocomplete="current-password"
				disabled={deleteLoading}
				class="w-full px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white disabled:opacity-50"
				data-testid="delete-account-password"
			/>
		</div>

		<div class="flex justify-end gap-3">
			<button
				type="button"
				onclick={closeDeleteModal}
				disabled={deleteLoading}
				class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
			>
				Cancel
			</button>
			<button
				type="button"
				onclick={handleDeleteAccount}
				disabled={deleteLoading || !deletePassword}
				class="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed"
				data-testid="delete-account-confirm"
			>
				{deleteLoading ? 'Deleting...' : 'Delete my account'}
			</button>
		</div>
	</div>
</Modal>
