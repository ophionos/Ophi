<script lang="ts">
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { api, ApiError } from '$lib/api/client';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { Package, LoaderCircle, Eye, EyeOff } from 'lucide-svelte';
	import ThemeToggle from '$lib/components/shared/ThemeToggle.svelte';
	import FormError from '$lib/components/shared/FormError.svelte';
	import type { PageData } from './$types';

	// Optional so the page also renders standalone (component tests); SvelteKit always passes it.
	let { data }: { data?: PageData } = $props();
	const registrationOpen = $derived(data?.registrationOpen ?? true);

	let name = $state('');
	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	// Registration asks for a complex password twice; without a reveal there is no way to check
	// what you typed. `type` can't be dynamic alongside bind:value, hence the explicit oninput.
	let showPassword = $state(false);
	let showConfirmPassword = $state(false);
	let error = $state('');
	let fieldErrors = $state<Record<string, string[]>>({});
	let loading = $state(false);

	function getFieldError(field: string): string[] {
		return fieldErrors[field] ?? [];
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();

		fieldErrors = {};

		if (password !== confirmPassword) {
			fieldErrors = { confirmPassword: ['Passwords do not match'] };
			return;
		}

		loading = true;
		error = '';

		try {
			const user = await api.register(email, password, name);
			auth.login(user);
			toast.success('Account created!');
			goto(resolve('/dashboard'));
		} catch (err) {
			if (err instanceof ApiError && err.hasFieldErrors()) {
				fieldErrors = err.details;
				error = '';
			} else {
				error = err instanceof Error ? err.message : 'Registration failed';
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
				{registrationOpen ? 'Create your account' : 'Sign-up is closed'}
			</h2>
		</div>

		<div class="bg-white dark:bg-gray-800 py-8 px-6 shadow-sm rounded-lg">
			{#if registrationOpen}
				<form onsubmit={handleSubmit} class="space-y-6">
					{#if error}
						<FormError variant="box" message={error} />
					{/if}

					<div>
						<label
							for="name"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Name
						</label>
						<input
							type="text"
							id="name"
							bind:value={name}
							required
							aria-invalid={getFieldError('name').length > 0}
							aria-describedby={getFieldError('name').length > 0 ? 'name-errors' : undefined}
							class="w-full px-3 py-2 border rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
								'name'
							).length > 0
								? 'border-red-300 dark:border-red-600 bg-red-50 dark:bg-red-900/20'
								: 'border-gray-300 dark:border-gray-600'}"
						/>
						{#if getFieldError('name').length > 0}
							<div role="alert">
								<ul id="name-errors" class="mt-1 text-xs text-red-600 dark:text-red-400">
									{#each getFieldError('name') as err, i (i)}
										<li>{err}</li>
									{/each}
								</ul>
							</div>
						{/if}
					</div>

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
							aria-invalid={getFieldError('email').length > 0}
							aria-describedby={getFieldError('email').length > 0 ? 'email-errors' : undefined}
							class="w-full px-3 py-2 border rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
								'email'
							).length > 0
								? 'border-red-300 dark:border-red-600 bg-red-50 dark:bg-red-900/20'
								: 'border-gray-300 dark:border-gray-600'}"
						/>
						{#if getFieldError('email').length > 0}
							<div role="alert">
								<ul id="email-errors" class="mt-1 text-xs text-red-600 dark:text-red-400">
									{#each getFieldError('email') as err, i (i)}
										<li>{err}</li>
									{/each}
								</ul>
							</div>
						{/if}
					</div>

					<div>
						<label
							for="password"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Password
						</label>
						<div class="relative">
							<input
								type={showPassword ? 'text' : 'password'}
								id="password"
								value={password}
								oninput={(e) => (password = e.currentTarget.value)}
								required
								minlength="8"
								aria-invalid={getFieldError('password').length > 0}
								aria-describedby={getFieldError('password').length > 0
									? 'password-errors'
									: 'password-hint'}
								class="w-full px-3 py-2 pr-10 border rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
									'password'
								).length > 0
									? 'border-red-300 dark:border-red-600 bg-red-50 dark:bg-red-900/20'
									: 'border-gray-300 dark:border-gray-600'}"
							/>
							<button
								type="button"
								onclick={() => (showPassword = !showPassword)}
								class="absolute inset-y-0 right-0 flex items-center px-3 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
								aria-label={showPassword ? 'Hide password' : 'Show password'}
								data-testid="toggle-password"
							>
								{#if showPassword}
									<EyeOff size={16} />
								{:else}
									<Eye size={16} />
								{/if}
							</button>
						</div>
						{#if getFieldError('password').length > 0}
							<div role="alert">
								<ul id="password-errors" class="mt-1 text-xs text-red-600 dark:text-red-400">
									{#each getFieldError('password') as err, i (i)}
										<li>{err}</li>
									{/each}
								</ul>
							</div>
						{:else}
							<p id="password-hint" class="mt-1 text-xs text-gray-500 dark:text-gray-400">
								At least 8 characters with uppercase, lowercase, and a number
							</p>
						{/if}
					</div>

					<div>
						<label
							for="confirmPassword"
							class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
						>
							Confirm Password
						</label>
						<div class="relative">
							<input
								type={showConfirmPassword ? 'text' : 'password'}
								id="confirmPassword"
								value={confirmPassword}
								oninput={(e) => (confirmPassword = e.currentTarget.value)}
								required
								aria-invalid={getFieldError('confirmPassword').length > 0}
								aria-describedby={getFieldError('confirmPassword').length > 0
									? 'confirmPassword-errors'
									: undefined}
								class="w-full px-3 py-2 pr-10 border rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
									'confirmPassword'
								).length > 0
									? 'border-red-300 dark:border-red-600 bg-red-50 dark:bg-red-900/20'
									: 'border-gray-300 dark:border-gray-600'}"
							/>
							<button
								type="button"
								onclick={() => (showConfirmPassword = !showConfirmPassword)}
								class="absolute inset-y-0 right-0 flex items-center px-3 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
								aria-label={showConfirmPassword ? 'Hide password' : 'Show password'}
								data-testid="toggle-confirm-password"
							>
								{#if showConfirmPassword}
									<EyeOff size={16} />
								{:else}
									<Eye size={16} />
								{/if}
							</button>
						</div>
						{#if getFieldError('confirmPassword').length > 0}
							<div role="alert">
								<ul id="confirmPassword-errors" class="mt-1 text-xs text-red-600 dark:text-red-400">
									{#each getFieldError('confirmPassword') as err, i (i)}
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
						Create account
					</button>
				</form>
			{:else}
				<p class="text-center text-gray-600 dark:text-gray-400">
					This server isn't accepting new accounts. If you need one, ask the person who runs it.
				</p>
			{/if}

			<p class="mt-6 text-center text-sm text-gray-600 dark:text-gray-400">
				Already have an account?
				<a
					href={resolve('/auth/login')}
					class="text-brand dark:text-brand-muted hover:text-brand dark:hover:text-brand-muted font-medium"
				>
					Sign in
				</a>
			</p>
		</div>
	</div>
</div>
