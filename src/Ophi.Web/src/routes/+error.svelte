<script lang="ts">
	import { page } from '$app/state';
	import { resolve } from '$app/paths';
	import { Compass, Home, ArrowLeft } from 'lucide-svelte';

	const status = $derived(page.status);
	const isNotFound = $derived(status === 404);
	const heading = $derived(isNotFound ? 'Page not found' : 'Something went wrong');
	const detail = $derived(
		isNotFound
			? "The page you're looking for doesn't exist or may have moved."
			: page.error?.message || 'An unexpected error occurred. Please try again.'
	);
</script>

<svelte:head>
	<title>{status} — Ophi</title>
</svelte:head>

<div class="flex flex-col items-center justify-center py-20 text-center">
	<div
		class="flex h-16 w-16 items-center justify-center rounded-2xl bg-brand-subtle dark:bg-brand-dark"
	>
		<Compass class="text-brand dark:text-brand-muted" size={32} />
	</div>

	<p class="mt-6 text-5xl font-bold tracking-tight text-gray-900 dark:text-white">{status}</p>
	<h1 class="mt-2 text-xl font-semibold text-gray-900 dark:text-white">{heading}</h1>
	<p class="mt-2 max-w-md text-sm text-gray-500 dark:text-gray-400">{detail}</p>

	<div class="mt-8 flex items-center gap-3">
		<button
			onclick={() => history.back()}
			class="inline-flex items-center gap-2 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-surface-2 dark:border-gray-700 dark:text-gray-300"
		>
			<ArrowLeft size={16} />
			Go back
		</button>
		<a
			href={resolve('/dashboard')}
			class="inline-flex items-center gap-2 rounded-lg bg-brand px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-hover"
		>
			<Home size={16} />
			Dashboard
		</a>
	</div>
</div>
