<script lang="ts">
	import { resolve } from '$app/paths';
	import {
		Link,
		ScanSearch,
		TrendingDown,
		Bell,
		BellRing,
		PiggyBank,
		Shield,
		Gauge
	} from 'lucide-svelte';
	import ThemeToggle from '$lib/components/shared/ThemeToggle.svelte';
	import OphiLogo from '$lib/components/shared/OphiLogo.svelte';
	import PriceChangeChip from '$lib/components/products/PriceChangeChip.svelte';
	import type { PageData } from './$types';

	// Optional so the page also renders standalone (component tests); SvelteKit always passes it.
	let { data }: { data?: PageData } = $props();
	const registrationOpen = $derived(data?.registrationOpen ?? true);

	// Signed-in users are redirected to /dashboard by this route's loader (+page.ts).

	// The hero shows the product itself: a static watchlist with the app's own chip and colours.
	const previewRows = [
		{ name: 'Kindle Paperwhite (16 GB)', store: 'amazon.com', price: '$139.00', change: -6.7 },
		{ name: 'Dyson V15 Detect', store: 'target.com', price: '$549.00', change: -1.2 },
		{ name: 'Nike Air Max 90', store: 'ebay.com', price: '$119.00', change: 2.1 }
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

	const features = [
		{
			icon: TrendingDown,
			title: 'Track Any Product',
			description:
				'Paste a URL and we auto-extract the price. Works with any product page, with tuned extraction for Amazon and eBay.'
		},
		{
			icon: Bell,
			title: 'Smart Alerts',
			description:
				'Set target prices, percentage drops, or price-above alerts. Get notified by email instantly.'
		},
		{
			icon: Gauge,
			title: 'Price in Context',
			description:
				"Each product shows a 30-day sparkline and where today's price sits in its 90-day range, so a real low is easy to spot."
		},
		{
			icon: Shield,
			title: 'Self-Hosted',
			description: 'Your data stays on your hardware. Runs on any Docker host.'
		}
	];

	const facts = [
		{ value: 'Any', label: 'Online store' },
		{ value: '24/7', label: 'Price monitoring' },
		{ value: '0', label: 'Data shared' }
	];

	const primaryButton =
		'inline-flex min-h-12 items-center justify-center rounded-lg bg-brand px-6 font-semibold text-white transition-colors hover:bg-brand-hover focus-visible-ring';
	const secondaryButton =
		'inline-flex min-h-12 items-center justify-center rounded-lg border border-gray-300 bg-surface-field px-6 font-semibold text-gray-900 transition-colors hover:bg-surface-field-hover focus-visible-ring dark:border-gray-600 dark:text-white';
</script>

<div class="relative min-h-screen overflow-hidden bg-surface-0">
	<!-- One soft brand wash behind the header and hero; everything below is flat. -->
	<div
		class="pointer-events-none absolute inset-x-0 top-0 h-[32rem] bg-linear-to-b from-brand/10 to-transparent dark:from-brand/15"
	></div>

	<header class="relative px-4 sm:px-6">
		<div class="mx-auto flex h-16 max-w-6xl items-center justify-between">
			<div class="flex items-center gap-2.5">
				<OphiLogo size={32} class="rounded" />
				<span class="text-xl font-bold tracking-tight text-gray-900 dark:text-white">Ophi</span>
			</div>
			<div class="flex items-center gap-1">
				<ThemeToggle />
				<a
					href={resolve('/auth/login')}
					class="inline-flex min-h-11 items-center rounded-md px-3 text-sm font-medium text-gray-700 transition-colors hover:bg-surface-2 hover:text-gray-900 focus-visible-ring dark:text-gray-300 dark:hover:text-white"
				>
					Sign In
				</a>
			</div>
		</div>
	</header>

	<!-- Hero -->
	<section class="relative px-4 pt-12 pb-20 sm:px-6 lg:pt-20 lg:pb-28">
		<div
			class="mx-auto grid max-w-6xl grid-cols-1 items-center gap-12 lg:grid-cols-[1.1fr_1fr] lg:gap-16"
		>
			<div class="min-w-0 text-center lg:text-left">
				<p
					class="mb-5 inline-flex items-center gap-2 rounded-full border border-gray-200 bg-surface-1 px-3 py-1 text-xs font-medium text-gray-600 dark:border-gray-700 dark:text-gray-300"
				>
					<Shield size={14} class="text-brand dark:text-brand-muted" aria-hidden="true" />
					Self-hosted price tracker
				</p>
				<h1
					class="text-4xl leading-[1.05] font-extrabold tracking-tight text-gray-900 sm:text-5xl lg:text-6xl dark:text-white"
				>
					Never pay <span class="text-brand dark:text-brand-muted">full price</span> again
				</h1>
				<p class="mx-auto mt-6 max-w-xl text-lg text-gray-600 lg:mx-0 dark:text-gray-300">
					Track prices from any online store. Get notified the moment they drop. Self-hosted,
					private, and completely yours.
				</p>
				<div class="mt-8 flex flex-col justify-center gap-3 sm:flex-row lg:justify-start">
					{#if registrationOpen}
						<a href={resolve('/auth/register')} class={primaryButton}>Get Started Free</a>
					{/if}
					<a
						href={resolve('/auth/login')}
						class={registrationOpen ? secondaryButton : primaryButton}
					>
						Sign In
					</a>
				</div>
			</div>

			<!-- Product preview: decorative, so it is hidden from assistive technology. -->
			<div class="relative mx-auto w-full max-w-md min-w-0 lg:max-w-none" aria-hidden="true">
				<div
					class="overflow-hidden rounded-xl border border-gray-200 bg-surface-1 shadow-xl dark:border-gray-700"
				>
					<div
						class="flex items-center justify-between border-b border-gray-200 px-5 py-3 dark:border-gray-700"
					>
						<span class="text-sm font-semibold text-gray-900 dark:text-white">Watchlist</span>
						<span class="text-xs text-gray-500 dark:text-gray-400">4 products</span>
					</div>

					<div class="border-b border-gray-200 p-5 dark:border-gray-700">
						<div class="flex items-start justify-between gap-4">
							<div class="min-w-0">
								<p class="truncate font-semibold text-gray-900 dark:text-white">
									Sony WH-1000XM5 Headphones
								</p>
								<p class="text-xs text-gray-500 dark:text-gray-400">amazon.com</p>
							</div>
							<PriceChangeChip change={-12.4} />
						</div>
						<div class="mt-4 flex items-end justify-between gap-4">
							<div>
								<p class="text-3xl font-bold tabular-nums text-gray-900 dark:text-white">$278.00</p>
								<p class="mt-1 text-xs text-gray-500 tabular-nums line-through dark:text-gray-400">
									$317.50
								</p>
							</div>
							<svg
								viewBox="0 0 200 56"
								class="h-14 w-44 text-price-drop"
								fill="none"
								preserveAspectRatio="none"
							>
								<path
									d="M0 10 H34 V16 H70 V12 H98 V24 H132 V30 H160 V46 H200 V56 H0 Z"
									fill="currentColor"
									opacity="0.12"
								/>
								<path
									d="M0 10 H34 V16 H70 V12 H98 V24 H132 V30 H160 V46 H200"
									stroke="currentColor"
									stroke-width="2"
									stroke-linejoin="round"
									vector-effect="non-scaling-stroke"
								/>
							</svg>
						</div>
						<div
							class="mt-4 flex items-center gap-2 rounded-lg bg-price-drop-subtle px-3 py-2 text-sm font-medium text-price-drop"
						>
							<BellRing size={16} />
							Alert reached: below $280.00
						</div>
					</div>

					<ul class="divide-y divide-gray-200 dark:divide-gray-700">
						{#each previewRows as row (row.name)}
							<li class="flex items-center gap-3 px-5 py-3">
								<span
									class="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-surface-2 text-sm font-semibold text-gray-500 dark:text-gray-400"
								>
									{row.name.charAt(0)}
								</span>
								<span class="min-w-0 flex-1">
									<span class="block truncate text-sm font-medium text-gray-900 dark:text-white">
										{row.name}
									</span>
									<span class="block text-xs text-gray-500 dark:text-gray-400">{row.store}</span>
								</span>
								<span class="text-sm font-semibold tabular-nums text-gray-900 dark:text-white">
									{row.price}
								</span>
								<PriceChangeChip change={row.change} />
							</li>
						{/each}
					</ul>
				</div>
			</div>
		</div>
	</section>

	<!-- How It Works -->
	<section class="relative border-t border-gray-200 px-4 py-20 sm:px-6 dark:border-gray-700">
		<div class="mx-auto max-w-6xl">
			<h2 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">How it works</h2>
			<p class="mt-2 text-gray-600 dark:text-gray-400">From URL to savings in under a minute</p>

			<ol class="mt-10 grid grid-cols-1 gap-x-6 gap-y-8 sm:grid-cols-2 md:grid-cols-5">
				{#each steps as step, i (step.title)}
					<li>
						<div class="flex items-center gap-3">
							<span
								class="flex h-10 w-10 items-center justify-center rounded-lg border border-gray-200 bg-surface-1 text-brand dark:border-gray-700 dark:text-brand-muted"
							>
								<step.icon size={20} aria-hidden="true" />
							</span>
							<span class="text-sm font-semibold text-gray-500 tabular-nums dark:text-gray-400">
								{String(i + 1).padStart(2, '0')}
							</span>
						</div>
						<h3 class="mt-4 font-semibold text-gray-900 dark:text-white">{step.title}</h3>
						<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">{step.description}</p>
					</li>
				{/each}
			</ol>
		</div>
	</section>

	<!-- Feature Cards -->
	<section class="px-4 pb-20 sm:px-6">
		<div class="mx-auto grid max-w-6xl gap-4 sm:grid-cols-2 lg:grid-cols-4">
			{#each features as feature (feature.title)}
				<div class="rounded-xl border border-gray-200 bg-surface-1 p-6 dark:border-gray-700">
					<div
						class="mb-4 flex h-10 w-10 items-center justify-center rounded-lg bg-brand-subtle text-brand dark:bg-brand-dark/40 dark:text-brand-muted"
					>
						<feature.icon size={20} aria-hidden="true" />
					</div>
					<h3 class="mb-2 font-semibold text-gray-900 dark:text-white">{feature.title}</h3>
					<p class="text-sm text-gray-600 dark:text-gray-400">{feature.description}</p>
				</div>
			{/each}
		</div>
	</section>

	<!-- Facts -->
	<section class="px-4 pb-20 sm:px-6">
		<dl
			class="mx-auto grid max-w-6xl grid-cols-3 divide-x divide-gray-200 rounded-xl border border-gray-200 bg-surface-1 dark:divide-gray-700 dark:border-gray-700"
		>
			{#each facts as fact (fact.label)}
				<div class="flex flex-col-reverse px-4 py-8 text-center">
					<dt class="mt-1 text-sm text-gray-600 dark:text-gray-400">{fact.label}</dt>
					<dd class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">
						{fact.value}
					</dd>
				</div>
			{/each}
		</dl>
	</section>

	<!-- Footer CTA: sign-up only, so it goes when the operator has closed registration -->
	{#if registrationOpen}
		<section class="border-t border-gray-200 px-4 py-20 sm:px-6 dark:border-gray-700">
			<div class="mx-auto max-w-2xl text-center">
				<h2 class="text-3xl font-bold tracking-tight text-gray-900 dark:text-white">
					Ready to start saving?
				</h2>
				<p class="mt-3 mb-8 text-gray-600 dark:text-gray-400">
					Set up in minutes. No credit card required. No data leaves your server.
				</p>
				<a href={resolve('/auth/register')} class={primaryButton}>Get Started Free</a>
			</div>
		</section>
	{/if}
</div>
