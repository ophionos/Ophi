<script lang="ts">
	import { resolve } from '$app/paths';
	import {
		Link,
		ScanSearch,
		TrendingDown,
		Bell,
		PiggyBank,
		Shield,
		Gauge
	} from 'lucide-svelte';
	import ThemeToggle from '$lib/components/shared/ThemeToggle.svelte';
	import OphiLogo from '$lib/components/shared/OphiLogo.svelte';
	import type { PageData } from './$types';

	// Optional so the page also renders standalone (component tests); SvelteKit always passes it.
	let { data }: { data?: PageData } = $props();
	const registrationOpen = $derived(data?.registrationOpen ?? true);

	// Signed-in users are redirected to /dashboard by this route's loader (+page.ts).

	const tickerItems = [
		{ name: 'AirPods Pro', price: '$189' },
		{ name: 'PS5 Console', price: '$449' },
		{ name: 'Kindle Paperwhite', price: '$139' },
		{ name: 'Samsung 4K TV', price: '$397' },
		{ name: 'Nike Air Max', price: '$119' },
		{ name: 'MacBook Air', price: '$999' },
		{ name: 'Instant Pot', price: '$59' },
		{ name: 'Sony WH-1000XM5', price: '$278' },
		{ name: 'iPad Mini', price: '$379' },
		{ name: 'Dyson V15', price: '$549' }
	];

	const steps = [
		{
			icon: Link,
			title: 'Paste a URL',
			description: 'Drop any product link from your favourite store'
		},
		{
			icon: ScanSearch,
			title: 'Auto-extract',
			description: 'We detect the price, name, and image instantly'
		},
		{
			icon: TrendingDown,
			title: 'Track drops',
			description: 'Prices are checked automatically on your schedule'
		},
		{
			icon: Bell,
			title: 'Get notified',
			description: 'Instant alerts when the price hits your target'
		},
		{
			icon: PiggyBank,
			title: 'Save money',
			description: 'Buy at the right time, every time'
		}
	];
</script>

<!-- Theme toggle -->
<div class="fixed top-4 right-4 z-50">
	<ThemeToggle />
</div>

<div class="min-h-screen bg-surface-0 overflow-hidden">
	<!-- Hero Section -->
	<section class="relative pt-20 pb-24 px-4">
		<!-- Subtle gradient overlay -->
		<div
			class="absolute inset-0 bg-linear-to-br from-brand-light via-transparent to-indigo-50/50 dark:from-brand-dark/20 dark:via-transparent dark:to-indigo-950/20 pointer-events-none"
		></div>

		<div class="relative max-w-5xl mx-auto text-center">
			<!-- Logo + Brand -->
			<div class="flex justify-center items-center gap-3 mb-8 animate-fade-in-up">
				<OphiLogo size={56} />
				<span class="text-4xl font-bold text-gray-900 dark:text-white tracking-tight">Ophi</span>
			</div>

			<!-- Tagline -->
			<h1
				class="text-4xl sm:text-5xl md:text-6xl font-extrabold text-gray-900 dark:text-white mb-6 leading-tight animate-fade-in-up"
				style="animation-delay: 100ms"
			>
				Never pay <span class="bg-brand-gradient bg-clip-text text-transparent">full price</span> again
			</h1>

			<p
				class="text-lg sm:text-xl text-gray-600 dark:text-gray-300 max-w-2xl mx-auto mb-10 animate-fade-in-up"
				style="animation-delay: 200ms"
			>
				Track prices from any online store. Get notified the moment they drop. Self-hosted, private, and completely yours.
			</p>

			<!-- CTAs -->
			<div
				class="flex flex-col sm:flex-row justify-center gap-4 animate-fade-in-up"
				style="animation-delay: 300ms"
			>
				{#if registrationOpen}
					<a
						href={resolve('/auth/register')}
						class="px-8 py-4 bg-brand-gradient text-white rounded-xl hover:opacity-90 font-semibold text-lg shadow-lg shadow-brand/20 transition-all duration-200 hover:shadow-xl hover:shadow-brand/30 hover:-translate-y-0.5"
					>
						Get Started Free
					</a>
				{/if}
				<a
					href={resolve('/auth/login')}
					class="px-8 py-4 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-200 rounded-xl hover:bg-gray-50 dark:hover:bg-gray-700 font-semibold text-lg border border-gray-200 dark:border-gray-700 transition-all duration-200 hover:-translate-y-0.5"
				>
					Sign In
				</a>
			</div>
		</div>
	</section>

	<!-- Price Ticker Tape -->
	<section class="relative py-4 bg-gray-50/80 dark:bg-gray-800/50 border-y border-gray-200/50 dark:border-gray-700/30 overflow-hidden" aria-hidden="true">
		<div class="animate-ticker flex whitespace-nowrap">
			{#each [...tickerItems, ...tickerItems] as item, i (i)}
				<span class="inline-flex items-center gap-2 mx-6 text-sm font-medium text-gray-400 dark:text-gray-500" aria-hidden="true">
					<span class="text-gray-300 dark:text-gray-600">{item.name}</span>
					<span class="text-brand dark:text-brand-muted font-semibold">{item.price}</span>
					{#if i < tickerItems.length * 2 - 1}
						<span class="text-gray-200 dark:text-gray-700 ml-4">·</span>
					{/if}
				</span>
			{/each}
		</div>
	</section>

	<!-- How It Works -->
	<section class="py-20 px-4">
		<div class="max-w-5xl mx-auto">
			<h2 class="text-3xl font-bold text-center text-gray-900 dark:text-white mb-4">
				How it works
			</h2>
			<p class="text-center text-gray-500 dark:text-gray-400 mb-14 max-w-lg mx-auto">
				From URL to savings in under a minute
			</p>

			<div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-6 md:gap-4">
				{#each steps as step, i (step.title)}
					<div
						class="relative flex flex-col items-center text-center animate-fade-in-up"
						style="animation-delay: {i * 100}ms"
					>
						<!-- Step number + icon -->
						<div
							class="w-14 h-14 rounded-2xl flex items-center justify-center mb-3 {i === steps.length - 1
								? 'bg-brand-gradient text-white'
								: 'bg-gray-100 dark:bg-gray-800 text-brand dark:text-brand-muted'}"
						>
							<step.icon size={24} />
						</div>

						<!-- Connector arrow (hidden on last item and mobile) -->
						{#if i < steps.length - 1}
							<div
								class="hidden md:block absolute top-7 left-[calc(50%+28px)] w-[calc(100%-56px)] h-px bg-gray-200 dark:bg-gray-700"
							></div>
						{/if}

						<h3 class="text-sm font-semibold text-gray-900 dark:text-white mb-1">{step.title}</h3>
						<p class="text-xs text-gray-500 dark:text-gray-400 leading-relaxed">{step.description}</p>
					</div>
				{/each}
			</div>
		</div>
	</section>

	<!-- Feature Cards -->
	<section class="py-16 px-4 bg-gray-50/50 dark:bg-gray-800/20">
		<div class="max-w-5xl mx-auto">
			<div class="grid sm:grid-cols-2 lg:grid-cols-4 gap-6">
				<div
					class="bg-white dark:bg-gray-800 p-6 rounded-2xl shadow-sm border border-gray-100 dark:border-gray-700/50 hover:shadow-md transition-shadow duration-300 animate-fade-in-up"
				>
					<div
						class="w-10 h-10 bg-brand-subtle dark:bg-brand-dark rounded-xl flex items-center justify-center mb-4"
					>
						<TrendingDown class="text-brand dark:text-brand-muted" size={20} />
					</div>
					<h3 class="font-semibold text-gray-900 dark:text-white mb-2">Track Any Product</h3>
					<p class="text-sm text-gray-500 dark:text-gray-400">
						Paste a URL and we auto-extract the price. Works with any product page, with tuned
						extraction for Amazon and eBay.
					</p>
				</div>

				<div
					class="bg-white dark:bg-gray-800 p-6 rounded-2xl shadow-sm border border-gray-100 dark:border-gray-700/50 hover:shadow-md transition-shadow duration-300 animate-fade-in-up"
					style="animation-delay: 100ms"
				>
					<div
						class="w-10 h-10 bg-green-100 dark:bg-green-900/50 rounded-xl flex items-center justify-center mb-4"
					>
						<Bell class="text-green-600 dark:text-green-400" size={20} />
					</div>
					<h3 class="font-semibold text-gray-900 dark:text-white mb-2">Smart Alerts</h3>
					<p class="text-sm text-gray-500 dark:text-gray-400">
						Set target prices, percentage drops, or price-above alerts. Get notified by email instantly.
					</p>
				</div>

				<div
					class="bg-white dark:bg-gray-800 p-6 rounded-2xl shadow-sm border border-gray-100 dark:border-gray-700/50 hover:shadow-md transition-shadow duration-300 animate-fade-in-up"
					style="animation-delay: 200ms"
				>
					<div
						class="w-10 h-10 bg-amber-100 dark:bg-amber-900/50 rounded-xl flex items-center justify-center mb-4"
					>
						<Gauge class="text-amber-600 dark:text-amber-400" size={20} />
					</div>
					<h3 class="font-semibold text-gray-900 dark:text-white mb-2">Deal Score</h3>
					<p class="text-sm text-gray-500 dark:text-gray-400">
						A 0-100 score weighing how close the price sits to its own low, which way it's trending, and how near it is to your target. Open maths on your own history — no black box.
					</p>
				</div>

				<div
					class="bg-white dark:bg-gray-800 p-6 rounded-2xl shadow-sm border border-gray-100 dark:border-gray-700/50 hover:shadow-md transition-shadow duration-300 animate-fade-in-up"
					style="animation-delay: 300ms"
				>
					<div
						class="w-10 h-10 bg-purple-100 dark:bg-purple-900/50 rounded-xl flex items-center justify-center mb-4"
					>
						<Shield class="text-purple-600 dark:text-purple-400" size={20} />
					</div>
					<h3 class="font-semibold text-gray-900 dark:text-white mb-2">Self-Hosted</h3>
					<p class="text-sm text-gray-500 dark:text-gray-400">
						Your data stays on your hardware. Runs on any Docker host.
					</p>
				</div>
			</div>
		</div>
	</section>

	<!-- Stats bar -->
	<section class="py-14 px-4">
		<div class="max-w-3xl mx-auto text-center">
			<div class="grid grid-cols-3 gap-8">
				<div>
					<p class="text-3xl font-bold text-brand dark:text-brand-muted">Any</p>
					<p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Online store</p>
				</div>
				<div>
					<p class="text-3xl font-bold text-brand dark:text-brand-muted">24/7</p>
					<p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Price monitoring</p>
				</div>
				<div>
					<p class="text-3xl font-bold text-brand dark:text-brand-muted">0</p>
					<p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Data shared</p>
				</div>
			</div>
		</div>
	</section>

	<!-- Footer CTA: sign-up only, so it goes when the operator has closed registration -->
	{#if registrationOpen}
		<section class="py-16 px-4 border-t border-gray-100 dark:border-gray-800">
			<div class="max-w-2xl mx-auto text-center">
				<h2 class="text-2xl font-bold text-gray-900 dark:text-white mb-4">
					Ready to start saving?
				</h2>
				<p class="text-gray-500 dark:text-gray-400 mb-8">
					Set up in minutes. No credit card required. No data leaves your server.
				</p>
				<a
					href={resolve('/auth/register')}
					class="inline-block px-8 py-4 bg-brand-gradient text-white rounded-xl hover:opacity-90 font-semibold text-lg shadow-lg shadow-brand/20 transition-all duration-200 hover:shadow-xl hover:shadow-brand/30"
				>
					Get Started Free
				</a>
			</div>
		</section>
	{/if}
</div>
