<script lang="ts">
	import { LoaderCircle, Sparkles } from 'lucide-svelte';
	import SelectorListInput from './SelectorListInput.svelte';
	import type {
		Store,
		CreateStoreRequest,
		UpdateStoreRequest,
		StoreSelectors
	} from '$lib/api/client';
	import { api, ApiError } from '$lib/api/client';
	import FormError from '$lib/components/shared/FormError.svelte';

	interface Props {
		store?: Store;
		onSubmit: (data: CreateStoreRequest | UpdateStoreRequest) => Promise<void>;
		onCancel: () => void;
	}

	let { store, onSubmit, onCancel }: Props = $props();

	const isEdit = $derived(!!store);
	const builtInIds = ['amazon', 'ebay', 'generic'];

	let storeId = $state('');
	let name = $state('');
	let domainPatterns = $state<string[]>(['']);
	let selectors = $state<StoreSelectors>({
		priceSelectors: [''],
		nameSelectors: [''],
		imageSelectors: [''],
		priceRegexPatterns: [],
		imageRegexPatterns: [],
		priceJsonPaths: [],
		nameJsonPaths: [],
		imageJsonPaths: []
	});

	let priceLocale = $state('en-US');
	let requiresJavaScript = $state(false);
	let currencyOverrideEnabled = $state(false);
	let currencyOverride = $state('');
	let affiliateParamName = $state('');
	let affiliateTag = $state('');
	let customUserAgent = $state('');

	let loading = $state(false);
	let error = $state('');
	let fieldErrors = $state<Record<string, string[]>>({});
	let errorEl = $state<HTMLDivElement | undefined>();

	$effect(() => {
		if (error && errorEl) {
			errorEl.scrollIntoView?.({ behavior: 'smooth', block: 'nearest' });
		}
	});

	$effect(() => {
		if (store) {
			const storeSelectors = store.selectors;
			storeId = store.storeId ?? '';
			name = store.name ?? '';
			domainPatterns =
				store.domainPatterns && store.domainPatterns.length > 0 ? [...store.domainPatterns] : [''];
			selectors = {
				priceSelectors:
					storeSelectors?.priceSelectors && storeSelectors.priceSelectors.length > 0
						? [...storeSelectors.priceSelectors]
						: [''],
				nameSelectors:
					storeSelectors?.nameSelectors && storeSelectors.nameSelectors.length > 0
						? [...storeSelectors.nameSelectors]
						: [''],
				imageSelectors:
					storeSelectors?.imageSelectors && storeSelectors.imageSelectors.length > 0
						? [...storeSelectors.imageSelectors]
						: [''],
				priceRegexPatterns: storeSelectors?.priceRegexPatterns ?? [],
				imageRegexPatterns: storeSelectors?.imageRegexPatterns ?? [],
				priceJsonPaths: storeSelectors?.priceJsonPaths ?? [],
				nameJsonPaths: storeSelectors?.nameJsonPaths ?? [],
				imageJsonPaths: storeSelectors?.imageJsonPaths ?? []
			};
			priceLocale = store.priceLocale ?? 'en-US';
			requiresJavaScript = store.requiresJavaScript ?? false;
			currencyOverrideEnabled = !!store.currencyOverride;
			currencyOverride = store.currencyOverride ?? '';
			affiliateParamName = store.affiliateParamName ?? '';
			affiliateTag = store.affiliateTag ?? '';
			customUserAgent = store.customUserAgent ?? '';
		} else {
			storeId = '';
			name = '';
			domainPatterns = [''];
			selectors = {
				priceSelectors: [''],
				nameSelectors: [''],
				imageSelectors: [''],
				priceRegexPatterns: [],
				imageRegexPatterns: [],
				priceJsonPaths: [],
				nameJsonPaths: [],
				imageJsonPaths: []
			};
			priceLocale = 'en-US';
			requiresJavaScript = false;
			currencyOverrideEnabled = false;
			currencyOverride = '';
			affiliateParamName = '';
			affiliateTag = '';
			customUserAgent = '';
		}
	});

	// Auto-detect state
	let detectUrl = $state('');
	let detecting = $state(false);
	let detectError = $state('');
	let detectSuccess = $state('');

	function slugifyDomain(domain: string): string {
		return domain
			.replace(/\./g, '-')
			.replace(/[^a-z0-9-]/g, '')
			.slice(0, 50);
	}

	async function handleDetect() {
		if (!detectUrl.trim()) {
			detectError = 'Please enter a URL';
			return;
		}

		detecting = true;
		detectError = '';
		detectSuccess = '';

		try {
			const result = await api.detectStore(detectUrl.trim());

			if (result.success && result.selectors) {
				// Populate form fields
				if (result.domain) {
					storeId = slugifyDomain(result.domain);
					domainPatterns = [result.domain];
				}
				if (result.storeName) {
					name = result.storeName;
				}

				selectors = {
					priceSelectors: result.selectors.priceSelectors,
					nameSelectors: result.selectors.nameSelectors,
					imageSelectors: result.selectors.imageSelectors,
					priceRegexPatterns: result.selectors.priceRegexPatterns ?? [],
					imageRegexPatterns: [],
					priceJsonPaths: result.selectors.priceJsonPaths ?? [],
					nameJsonPaths: result.selectors.nameJsonPaths ?? [],
					imageJsonPaths: result.selectors.imageJsonPaths ?? []
				};

				const detected: string[] = [];
				if (result.selectors.priceSelectors.length > 0) detected.push('price');
				if (result.selectors.nameSelectors.length > 0) detected.push('name');
				if (result.selectors.imageSelectors.length > 0) detected.push('image');
				detectSuccess = `Detected ${detected.join(', ')} selectors from ${result.domain}`;
			} else {
				detectError = result.error ?? 'Could not detect store configuration';
			}
		} catch (err) {
			detectError = err instanceof Error ? err.message : 'Failed to detect store';
		} finally {
			detecting = false;
		}
	}

	function validateStoreId(value: string): string | null {
		if (!value) return 'Store ID is required';
		if (value.length < 3) return 'Store ID must be at least 3 characters';
		if (value.length > 50) return 'Store ID must be at most 50 characters';
		if (!/^[a-z0-9-]+$/.test(value))
			return 'Store ID can only contain lowercase letters, numbers, and hyphens';
		if (builtInIds.includes(value)) return 'This ID is reserved for built-in stores';
		return null;
	}

	function validateForm(): boolean {
		const errors: Record<string, string[]> = {};

		if (!isEdit) {
			const storeIdError = validateStoreId(storeId);
			if (storeIdError) errors.storeid = [storeIdError];
		}

		if (!name.trim()) errors.name = ['Name is required'];
		if (name.length > 100) errors.name = ['Name must be at most 100 characters'];

		const validDomains = domainPatterns.filter((d) => d.trim());
		if (validDomains.length === 0)
			errors.domainpatterns = ['At least one domain pattern is required'];

		const validPrice = selectors.priceSelectors.filter((s) => s.trim());
		if (validPrice.length === 0)
			errors['selectors.priceselectors'] = ['At least one price selector is required'];

		const validName = selectors.nameSelectors.filter((s) => s.trim());
		if (validName.length === 0)
			errors['selectors.nameselectors'] = ['At least one name selector is required'];

		const validImage = selectors.imageSelectors.filter((s) => s.trim());
		if (validImage.length === 0)
			errors['selectors.imageselectors'] = ['At least one image selector is required'];

		if (currencyOverrideEnabled && !/^[A-Z]{3}$/.test(currencyOverride))
			errors.currencyoverride = ['Currency must be a 3-letter uppercase code (e.g., EUR, USD)'];

		fieldErrors = errors;
		return Object.keys(errors).length === 0;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		if (!validateForm()) return;

		loading = true;
		error = '';
		fieldErrors = {};

		try {
			const cleanedSelectors: StoreSelectors = {
				priceSelectors: selectors.priceSelectors.filter((s) => s.trim()),
				nameSelectors: selectors.nameSelectors.filter((s) => s.trim()),
				imageSelectors: selectors.imageSelectors.filter((s) => s.trim()),
				priceRegexPatterns: selectors.priceRegexPatterns?.filter((s) => s.trim()),
				imageRegexPatterns: selectors.imageRegexPatterns?.filter((s) => s.trim()),
				priceJsonPaths: selectors.priceJsonPaths?.filter((s) => s.trim()),
				nameJsonPaths: selectors.nameJsonPaths?.filter((s) => s.trim()),
				imageJsonPaths: selectors.imageJsonPaths?.filter((s) => s.trim())
			};

			const resolvedCurrencyOverride = currencyOverrideEnabled ? currencyOverride : undefined;
			const resolvedAffiliateParamName = affiliateParamName.trim() || undefined;
			const resolvedAffiliateTag = affiliateTag.trim() || undefined;
			const resolvedCustomUserAgent = customUserAgent.trim() || undefined;

			if (isEdit) {
				const data: UpdateStoreRequest = {
					name: name.trim(),
					domainPatterns: domainPatterns.filter((d) => d.trim()),
					selectors: cleanedSelectors,
					priceLocale,
					requiresJavaScript,
					currencyOverride: resolvedCurrencyOverride,
					affiliateParamName: resolvedAffiliateParamName,
					affiliateTag: resolvedAffiliateTag,
					customUserAgent: resolvedCustomUserAgent
				};
				await onSubmit(data);
			} else {
				const data: CreateStoreRequest = {
					storeId: storeId.trim(),
					name: name.trim(),
					domainPatterns: domainPatterns.filter((d) => d.trim()),
					selectors: cleanedSelectors,
					priceLocale,
					requiresJavaScript,
					currencyOverride: resolvedCurrencyOverride,
					affiliateParamName: resolvedAffiliateParamName,
					affiliateTag: resolvedAffiliateTag,
					customUserAgent: resolvedCustomUserAgent
				};
				await onSubmit(data);
			}
		} catch (err) {
			if (err instanceof ApiError) {
				error = err.message;
				if (err.hasFieldErrors()) {
					fieldErrors = err.details;
				}
			} else {
				error = err instanceof Error ? err.message : 'An error occurred';
			}
		} finally {
			loading = false;
		}
	}

	function getFieldError(field: string): string | undefined {
		return fieldErrors[field.toLowerCase()]?.[0];
	}
</script>

<form onsubmit={handleSubmit} class="space-y-6">
	{#if error}
		<FormError variant="box" bind:element={errorEl} message={error} />
	{/if}

	<!-- Auto-detect from URL (create mode only) -->
	{#if !isEdit}
		<div class="space-y-3">
			<h4
				class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
			>
				Auto-detect from URL
			</h4>
			<p class="text-xs text-gray-500 dark:text-gray-400">
				Paste a product URL to automatically detect store selectors
			</p>
			<div class="flex gap-2">
				<input
					type="text"
					data-testid="detect-url-input"
					bind:value={detectUrl}
					placeholder="https://example.com/product/123"
					class="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent"
					disabled={detecting || loading}
				/>
				<button
					type="button"
					data-testid="detect-button"
					onclick={handleDetect}
					disabled={detecting || loading}
					class="px-4 py-2 text-sm font-medium text-white bg-purple-600 rounded-md hover:bg-purple-700 disabled:opacity-50 flex items-center gap-2 whitespace-nowrap"
				>
					{#if detecting}
						<LoaderCircle size={16} class="animate-spin" />
					{:else}
						<Sparkles size={16} />
					{/if}
					Detect
				</button>
			</div>
			{#if detectError}
				<FormError testid="detect-error" message={detectError} />
			{/if}
			{#if detectSuccess}
				<p
					class="text-sm text-green-600 dark:text-green-400"
					data-testid="detect-success"
				>
					{detectSuccess}
				</p>
			{/if}
		</div>
	{/if}

	<!-- Basic Info -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			Basic Information
		</h4>

		{#if !isEdit}
			<div>
				<label for="storeId" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
					Store ID <span class="text-red-500">*</span>
				</label>
				<input
					id="storeId"
					type="text"
					bind:value={storeId}
					placeholder="my-custom-store"
					class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
						'storeid'
					)
						? 'border-red-300 dark:border-red-600'
						: ''}"
					disabled={loading}
				/>
				<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
					Lowercase letters, numbers, and hyphens only
				</p>
				{#if getFieldError('storeid')}
					<p role="alert" class="mt-1 text-sm text-red-600 dark:text-red-400">{getFieldError('storeid')}</p>
				{/if}
			</div>
		{/if}

		<div>
			<label for="name" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Display Name <span class="text-red-500">*</span>
			</label>
			<input
				id="name"
				type="text"
				bind:value={name}
				placeholder="My Custom Store"
				class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent {getFieldError(
					'name'
				)
					? 'border-red-300 dark:border-red-600'
					: ''}"
				disabled={loading}
			/>
			{#if getFieldError('name')}
				<p role="alert" class="mt-1 text-sm text-red-600 dark:text-red-400">{getFieldError('name')}</p>
			{/if}
		</div>
	</div>

	<!-- Domain Patterns -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			Domain Patterns
		</h4>
		<SelectorListInput
			label="Domains"
			values={domainPatterns}
			onChange={(v) => (domainPatterns = v)}
			placeholder="example.com"
			required={true}
			helpText="Domain patterns to match URLs (e.g., 'example.com', 'shop.example.com')"
		/>
		{#if getFieldError('domainpatterns')}
			<p role="alert" class="text-sm text-red-600 dark:text-red-400">{getFieldError('domainpatterns')}</p>
		{/if}
	</div>

	<!-- Scraping Options -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			Scraping Options
		</h4>

		<div>
			<label
				for="priceLocale"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300"
			>
				Price Locale
			</label>
			<select
				id="priceLocale"
				data-testid="store-price-locale"
				bind:value={priceLocale}
				disabled={loading}
				class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-brand focus:border-transparent"
			>
				<option value="en-US">English - US (1,234.56)</option>
				<option value="en-GB">English - UK (1,234.56)</option>
				<option value="pt-PT">Portuguese - Portugal (1.234,56)</option>
				<option value="pt-BR">Portuguese - Brazil (1.234,56)</option>
				<option value="de-DE">German - Germany (1.234,56)</option>
				<option value="fr-FR">French - France (1 234,56)</option>
				<option value="es-ES">Spanish - Spain (1.234,56)</option>
				<option value="it-IT">Italian - Italy (1.234,56)</option>
				<option value="nl-NL">Dutch - Netherlands (1.234,56)</option>
				<option value="pl-PL">Polish - Poland (1 234,56)</option>
			</select>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Determines how prices are parsed (decimal and thousands separators)
			</p>
		</div>

		<div>
			<label class="flex items-center gap-2 cursor-pointer">
				<input
					type="checkbox"
					data-testid="store-requires-js"
					bind:checked={requiresJavaScript}
					disabled={loading}
					class="w-4 h-4 rounded border-gray-300 dark:border-gray-600 text-brand focus:ring-brand dark:bg-gray-700"
				/>
				<span class="text-sm font-medium text-gray-700 dark:text-gray-300">
					Requires JavaScript (use browser rendering)
				</span>
			</label>
			<p class="mt-1 ml-6 text-xs text-gray-500 dark:text-gray-400">
				Enable if the store blocks automated requests or renders prices with JavaScript
			</p>
		</div>

		<div>
			<label class="flex items-center gap-2 cursor-pointer">
				<input
					type="checkbox"
					data-testid="currency-override-checkbox"
					bind:checked={currencyOverrideEnabled}
					disabled={loading}
					class="w-4 h-4 rounded border-gray-300 dark:border-gray-600 text-brand focus:ring-brand dark:bg-gray-700"
				/>
				<span class="text-sm font-medium text-gray-700 dark:text-gray-300">
					Override currency
				</span>
			</label>
			<p class="mt-1 ml-6 text-xs text-gray-500 dark:text-gray-400">
				Force a specific currency instead of auto-detecting from the page
			</p>
			{#if currencyOverrideEnabled}
				<input
					type="text"
					data-testid="currency-override-input"
					bind:value={currencyOverride}
					placeholder="EUR"
					maxlength={3}
					disabled={loading}
					class="mt-2 ml-6 w-24 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent uppercase {getFieldError('currencyoverride') ? 'border-red-300 dark:border-red-600' : ''}"
				/>
				{#if getFieldError('currencyoverride')}
					<p role="alert" class="mt-1 ml-6 text-sm text-red-600 dark:text-red-400">
						{getFieldError('currencyoverride')}
					</p>
				{/if}
			{/if}
		</div>
		<div>
			<label for="customUserAgent" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Custom User-Agent
			</label>
			<input
				id="customUserAgent"
				type="text"
				data-testid="custom-user-agent"
				bind:value={customUserAgent}
				placeholder="Leave empty for automatic browser rotation"
				maxlength={500}
				disabled={loading}
				class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent font-mono text-xs"
			/>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Override the browser identity for scraping this store. Leave blank to rotate through modern browser profiles automatically.
			</p>
		</div>
	</div>

	<!-- Affiliate Configuration -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			Affiliate Configuration
			<span class="text-gray-400 dark:text-gray-500 font-normal">(Optional)</span>
		</h4>
		<p class="text-xs text-gray-500 dark:text-gray-400">
			Add affiliate parameters to product URLs when clicking through to this store
		</p>

		<div>
			<label for="affiliateParamName" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Parameter Name
			</label>
			<input
				id="affiliateParamName"
				type="text"
				data-testid="affiliate-param-name"
				bind:value={affiliateParamName}
				placeholder="tag"
				maxlength={50}
				disabled={loading}
				class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent"
			/>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				The query parameter name (e.g., "tag", "ref", "aff")
			</p>
		</div>

		<div>
			<label for="affiliateTag" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Affiliate Tag
			</label>
			<input
				id="affiliateTag"
				type="text"
				data-testid="affiliate-tag"
				bind:value={affiliateTag}
				placeholder="my-affiliate-20"
				maxlength={100}
				disabled={loading}
				class="mt-1 w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-brand focus:border-transparent"
			/>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Your affiliate tag value (e.g., "ophi-20")
			</p>
		</div>
	</div>

	<!-- Selectors -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			CSS Selectors
		</h4>

		<SelectorListInput
			label="Price Selectors"
			values={selectors.priceSelectors}
			onChange={(v) => (selectors.priceSelectors = v)}
			placeholder="#price, .product-price, [data-price]"
			required={true}
			helpText="CSS selectors to find the product price"
		/>
		{#if getFieldError('selectors.priceselectors')}
			<p role="alert" class="text-sm text-red-600 dark:text-red-400">
				{getFieldError('selectors.priceselectors')}
			</p>
		{/if}

		<SelectorListInput
			label="Name Selectors"
			values={selectors.nameSelectors}
			onChange={(v) => (selectors.nameSelectors = v)}
			placeholder="h1, .product-title, [data-product-name]"
			required={true}
			helpText="CSS selectors to find the product name"
		/>
		{#if getFieldError('selectors.nameselectors')}
			<p role="alert" class="text-sm text-red-600 dark:text-red-400">
				{getFieldError('selectors.nameselectors')}
			</p>
		{/if}

		<SelectorListInput
			label="Image Selectors"
			values={selectors.imageSelectors}
			onChange={(v) => (selectors.imageSelectors = v)}
			placeholder=".product-image img, #main-image"
			required={true}
			helpText="CSS selectors to find the product image"
		/>
		{#if getFieldError('selectors.imageselectors')}
			<p role="alert" class="text-sm text-red-600 dark:text-red-400">
				{getFieldError('selectors.imageselectors')}
			</p>
		{/if}
	</div>

	<!-- Optional JSONPath Expressions -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			JSONPath Expressions
			<span class="text-gray-400 dark:text-gray-500 font-normal">(Optional)</span>
		</h4>
		<p class="text-xs text-gray-500 dark:text-gray-400">
			Extract data from JSON-LD structured data embedded in the page (e.g., Schema.org Product
			data). Applied after CSS selectors and before regex patterns.
		</p>

		<SelectorListInput
			label="Price JSONPath"
			values={selectors.priceJsonPaths ?? []}
			onChange={(v) => (selectors.priceJsonPaths = v)}
			placeholder="$.offers.price"
			helpText="JSONPath expressions to extract price from JSON-LD data"
		/>

		<SelectorListInput
			label="Name JSONPath"
			values={selectors.nameJsonPaths ?? []}
			onChange={(v) => (selectors.nameJsonPaths = v)}
			placeholder="$.name"
			helpText="JSONPath expressions to extract product name from JSON-LD data"
		/>

		<SelectorListInput
			label="Image JSONPath"
			values={selectors.imageJsonPaths ?? []}
			onChange={(v) => (selectors.imageJsonPaths = v)}
			placeholder="$.image"
			helpText="JSONPath expressions to extract image URL from JSON-LD data"
		/>
	</div>

	<!-- Optional Regex Patterns -->
	<div class="space-y-4">
		<h4
			class="text-sm font-medium text-gray-900 dark:text-white border-b border-gray-200 dark:border-gray-700 pb-2"
		>
			Regex Patterns <span class="text-gray-400 dark:text-gray-500 font-normal">(Optional)</span>
		</h4>

		<SelectorListInput
			label="Price Regex Patterns"
			values={selectors.priceRegexPatterns ?? []}
			onChange={(v) => (selectors.priceRegexPatterns = v)}
			placeholder="\\$([\\d,]+\\.\\d{2})"
			helpText="Fallback regex patterns to extract price from page content"
		/>

		<SelectorListInput
			label="Image Regex Patterns"
			values={selectors.imageRegexPatterns ?? []}
			onChange={(v) => (selectors.imageRegexPatterns = v)}
			placeholder="https://.*\\.(?:jpg|png|webp)"
			helpText="Fallback regex patterns to extract image URLs"
		/>
	</div>

	<!-- Actions -->
	<div class="flex justify-end gap-3 pt-4 border-t border-gray-200 dark:border-gray-700">
		<button
			type="button"
			onclick={onCancel}
			disabled={loading}
			class="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-md hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
		>
			Cancel
		</button>
		<button
			type="submit"
			disabled={loading}
			class="px-4 py-2 text-sm font-medium text-white bg-brand rounded-md hover:bg-brand-hover disabled:opacity-50 flex items-center gap-2"
		>
			{#if loading}
				<LoaderCircle size={16} class="animate-spin" />
			{/if}
			{isEdit ? 'Update Store' : 'Create Store'}
		</button>
	</div>
</form>
