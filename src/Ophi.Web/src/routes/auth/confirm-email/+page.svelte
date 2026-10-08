<script lang="ts">
	import { page } from '$app/state';
	import { resolve } from '$app/paths';
	import { api } from '$lib/api/client';
	import { Package, LoaderCircle } from 'lucide-svelte';
	import ThemeToggle from '$lib/components/shared/ThemeToggle.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	let token = $derived(page.url.searchParams.get('token') ?? '');
	let error = $state('');
	let loading = $state(false);
	let confirmedEmail = $state('');

	// Confirms only on a click: mail link scanners open links, and some run scripts.
	async function handleConfirm() {
		error = '';
		loading = true;
		try {
			const result = await api.confirmEmailChange(token);
			confirmedEmail = result.email;
		} catch (err) {
			error = err instanceof Error ? err.message : 'Something went wrong';
		} finally {
			loading = false;
		}
	}
</script>

<svelte:head>
	<title>Confirm Email - Ophi</title>
</svelte:head>

<!-- Theme toggle in top-right corner -->
<div class="fixed top-4 right-4 z-50">
	<ThemeToggle />
</div>

<div class="min-h-screen bg-gray-50 dark:bg-gray-900 flex items-center justify-center py-12 px-4">
	<div class="max-w-md w-full">
		<div class="text-center mb-8">
			<a
				href={resolve('/')}
				class="inline-flex items-center gap-2 text-2xl font-bold text-brand dark:text-brand-muted"
			>
				<Package size={32} />
				Ophi
			</a>
			<h2 class="mt-4 text-2xl font-semibold text-gray-900 dark:text-white">
				Confirm your new email
			</h2>
		</div>

		<div class="bg-white dark:bg-gray-800 py-8 px-6 shadow-sm rounded-lg space-y-6">
			{#if confirmedEmail}
				<div
					class="p-4 bg-green-50 dark:bg-green-900/30 border border-green-200 dark:border-green-800 rounded-lg text-sm text-green-700 dark:text-green-400"
					data-testid="success-message"
				>
					Your sign-in email is now <strong>{confirmedEmail}</strong>.
				</div>

				<p class="text-center text-sm text-gray-600 dark:text-gray-400">
					<a
						href={resolve('/dashboard')}
						class="text-brand dark:text-brand-muted hover:text-brand dark:hover:text-brand-muted font-medium"
					>
						Continue to Ophi
					</a>
				</p>
			{:else if !token}
				<FormError
					variant="box"
					message="This confirmation link is incomplete. Open the link from your email again."
				/>
			{:else}
				{#if error}
					<FormError variant="box" message={error} />
				{/if}

				<p class="text-sm text-gray-600 dark:text-gray-400">
					Confirm to make this address the email you sign in with.
				</p>

				<button
					type="button"
					onclick={handleConfirm}
					disabled={loading}
					class="w-full py-2 px-4 bg-brand text-white rounded-lg hover:bg-brand-hover disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
				>
					{#if loading}
						<LoaderCircle size={18} class="animate-spin" />
					{/if}
					Confirm email change
				</button>
			{/if}
		</div>
	</div>
</div>
