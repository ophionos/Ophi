import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/svelte';
import StoresPage from '../../../routes/stores/+page.svelte';
import { page } from '$app/state';
import { replaceState } from '$app/navigation';
import type { Store } from '$lib/api/client';
import type { PageData } from '../../../routes/stores/$types';

// The page imports the api client; none of its methods are called during mount, but the
// module must exist and not throw at import time.
vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			updateSettings: vi.fn(),
			testDiscordWebhook: vi.fn(),
			getWebhookTargets: vi.fn(),
			listApiKeys: vi.fn(),
			getStores: vi.fn(),
			updateStore: vi.fn(),
			createStore: vi.fn(),
			deleteStore: vi.fn()
		}
	};
});

const amazonStore: Store = {
	id: 'store-1',
	storeId: 'amazon',
	name: 'Amazon',
	domainPatterns: ['amazon.com'],
	selectors: {
		priceSelectors: ['.a-price'],
		nameSelectors: ['#title'],
		imageSelectors: ['#img']
	},
	isBuiltIn: true,
	priceLocale: 'en-US',
	requiresJavaScript: false
};

// A user-defined (editable) store — auto-open is only valid for these.
const customStore: Store = {
	id: 'store-2',
	storeId: 'mystore',
	name: 'My Store',
	domainPatterns: ['mystore.com'],
	selectors: {
		priceSelectors: ['.price'],
		nameSelectors: ['.name'],
		imageSelectors: ['.img']
	},
	isBuiltIn: false,
	priceLocale: 'en-US',
	requiresJavaScript: false
};

function renderStores(editParam?: string, storeList: Store[] = [customStore]) {
	page.url = new URL(
		editParam
			? `http://localhost/stores?edit=${encodeURIComponent(editParam)}`
			: 'http://localhost/stores'
	) as unknown as typeof page.url;

	return render(StoresPage, {
		props: {
			data: {
				user: { id: '1', email: 'a@b.com', name: 'A' },
				stores: storeList
			} as unknown as PageData
		}
	});
}

describe('Store Configurations Page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		page.url = new URL('http://localhost/stores') as unknown as typeof page.url;
	});

	it('should show the empty state with an add action when no stores exist', () => {
		renderStores(undefined, []);
		expect(screen.getByText('No stores configured')).toBeInTheDocument();
		expect(screen.getByRole('button', { name: 'Add your first store' })).toBeInTheDocument();
	});

	it('should auto-open the edit modal when arrived at via ?edit=<domain>', async () => {
		// A passing render here is itself the regression guard: the old code looped forever
		// (effect_update_depth_exceeded) when ?edit matched a store.
		renderStores('www.mystore.com');

		await waitFor(() => {
			expect(screen.getByText('Edit Store Configuration')).toBeInTheDocument();
		});
	});

	it('should strip the ?edit param after auto-opening so it cannot reopen', async () => {
		renderStores('www.mystore.com');

		await waitFor(() => expect(replaceState).toHaveBeenCalled());
		const urlArg = vi.mocked(replaceState).mock.calls[0][0] as URL;
		expect(new URL(urlArg, 'http://localhost').searchParams.has('edit')).toBe(false);
	});

	it('should stay closed after the modal is dismissed', async () => {
		renderStores('www.mystore.com');

		await waitFor(() => expect(screen.getByText('Edit Store Configuration')).toBeInTheDocument());

		await fireEvent.click(screen.getByRole('button', { name: 'Close' }));

		await waitFor(() =>
			expect(screen.queryByText('Edit Store Configuration')).not.toBeInTheDocument()
		);
	});

	it('should not open the edit modal for a built-in store, but still strips ?edit', async () => {
		// Built-in stores are not editable, so a ?edit deep-link to one must be a no-op for the
		// modal — while still cleaning the URL so the param can't linger.
		renderStores('www.amazon.com', [amazonStore]);

		await waitFor(() => expect(replaceState).toHaveBeenCalled());
		expect(screen.queryByText('Edit Store Configuration')).not.toBeInTheDocument();
		const urlArg = vi.mocked(replaceState).mock.calls[0][0] as URL;
		expect(new URL(urlArg, 'http://localhost').searchParams.has('edit')).toBe(false);
	});

	it('should not open the edit modal without an ?edit param', async () => {
		renderStores();

		// Give effects/onMount a tick to run.
		await Promise.resolve();
		expect(screen.queryByText('Edit Store Configuration')).not.toBeInTheDocument();
		expect(replaceState).not.toHaveBeenCalled();
	});
});
