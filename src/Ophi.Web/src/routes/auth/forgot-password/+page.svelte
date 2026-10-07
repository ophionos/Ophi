<script lang="ts">
	import { resolve } from '$app/paths';
	import { api, ApiError } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';
	import { Package, LoaderCircle } from 'lucide-svelte';
	import ThemeToggle from '$lib/components/shared/ThemeToggle.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';

	let email = $state('');
	let error = $state('');
	let fieldErrors = $state<Record<string, string[]>>({});
	let loading = $state(false);
	let submitted = $state(false);

	function getFieldError(field: string): string[] {
		return fieldErrors[field] ?? [];
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		loading = true;
		error = '';
		fieldErrors = {};

		try {
			await api.forgotPassword(email);
			submitted = true;
			toast.success('Reset link sent!');
		} catch (err) {
			if (err instanceof ApiError && err.hasFieldErrors()) {
				fieldErrors = err.details;
			} else {
				error = err instanceof Error ? err.message : 'Something went wrong';
			}
		} finally {
			loading = false;
		}
	}
</script>

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
				Reset your password
			</h2>
		</div>

		<div class="bg-white dark:bg-gray-800 py-8 px-6 shadow-sm rounded-lg">
			{#if submitted}
				<div
					class="p-4 bg-green-50 dark:bg-green-900/30 border border-green-200 dark:border-green-800 rounded-lg text-sm text-green-700 dark:text-green-400"
					data-testid="success-message"
				>
					If an account with that email exists, a password reset link has been sent. Please
					check your inbox.
				</div>

				<p class="mt-6 text-center text-sm text-gray-600 dark:text-gray-400">
					<a
						href={resolve('/auth/login')}
						class="text-brand dark:text-brand-muted hover:text-brand dark:hover:text-brand-muted font-medium"
					>
						Back to sign in
					</a>
				</p>
			{:else}
				<form onsubmit={handleSubmit} class="space-y-6">
					{#if error}
						<FormError variant="box" message={error} />
					{/if}

					<p class="text-sm text-gray-600 dark:text-gray-400">
						Enter your email address and we'll send you a link to reset your password.
					</p>

					<div>
						<label
							for="email"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Email address
						</label>
						<input
							type="email"
							id="email"
							bind:value={email}
							required
							class="w-full px-3 py-2 border rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
								'email'
							).length > 0
								? 'border-red-300 dark:border-red-600 bg-red-50 dark:bg-red-900/20'
								: 'border-gray-300 dark:border-gray-600'}"
						/>
						{#if getFieldError('email').length > 0}
							<div role="alert">
								<ul class="mt-1 text-xs text-red-600 dark:text-red-400">
									{#each getFieldError('email') as err, i (i)}
										<li>{err}</li>
									{/each}
								</ul>
							</div>
						{/if}
					</div>

					<button
						type="submit"
						disabled={loading}
						class="w-full py-2 px-4 bg-brand text-white rounded-lg hover:bg-brand-hover disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
					>
						{#if loading}
							<LoaderCircle size={18} class="animate-spin" />
						{/if}
						Send reset link
					</button>
				</form>

				<p class="mt-6 text-center text-sm text-gray-600 dark:text-gray-400">
					Remember your password?
					<a
						href={resolve('/auth/login')}
						class="text-brand dark:text-brand-muted hover:text-brand dark:hover:text-brand-muted font-medium"
					>
						Sign in
					</a>
				</p>
			{/if}
		</div>
	</div>
</div>
