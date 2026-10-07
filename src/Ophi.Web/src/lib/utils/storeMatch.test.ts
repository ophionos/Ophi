import { describe, it, expect } from 'vitest';
import { getHostname, findStoreForDomain, isBuiltInStoreUrl } from './storeMatch';
import type { Store } from '$lib/api/client';

const builtInAmazon: Store = {
	id: undefined, // built-in stores have no DB row / id
	storeId: 'amazon',
	name: 'Amazon',
	domainPatterns: ['amazon.com'],
	selectors: { priceSelectors: ['.a-price'], nameSelectors: ['#title'], imageSelectors: ['#img'] },
	isBuiltIn: true,
	priceLocale: 'en-US',
	requiresJavaScript: false
};

const customStore: Store = {
	id: 'store-1',
	storeId: 'mystore',
	name: 'My Store',
	domainPatterns: ['mystore.com'],
	selectors: { priceSelectors: ['.price'], nameSelectors: ['.name'], imageSelectors: ['.img'] },
	isBuiltIn: false,
	priceLocale: 'en-US',
	requiresJavaScript: false
};

const stores = [builtInAmazon, customStore];

describe('getHostname', () => {
	it('should extract the hostname when given a valid URL', () => {
		expect(getHostname('https://www.amazon.com/dp/B001')).toBe('www.amazon.com');
	});

	it('should fall back to the raw string when the URL cannot be parsed', () => {
		expect(getHostname('not a url')).toBe('not a url');
	});
});

describe('findStoreForDomain', () => {
	it('should match a store on an exact domain', () => {
		expect(findStoreForDomain(stores, 'amazon.com')).toBe(builtInAmazon);
	});

	it('should match a store on a subdomain of a pattern', () => {
		expect(findStoreForDomain(stores, 'www.amazon.com')).toBe(builtInAmazon);
	});

	it('should match case-insensitively', () => {
		expect(findStoreForDomain(stores, 'WWW.MyStore.COM')).toBe(customStore);
	});

	it('should return undefined when no store matches', () => {
		expect(findStoreForDomain(stores, 'unknown.example')).toBeUndefined();
	});

	it('should not match a different domain that merely ends with the pattern text', () => {
		// "notamazon.com" must NOT match the "amazon.com" pattern (only "*.amazon.com" or exact).
		expect(findStoreForDomain(stores, 'notamazon.com')).toBeUndefined();
	});
});

describe('isBuiltInStoreUrl', () => {
	it('should return true for a built-in store URL — purely from the domain, no storeId needed', () => {
		// A freshly-added (pending) product has no scraped storeId yet; matching on domain
		// alone must still identify it as built-in so the edit affordance stays hidden.
		expect(isBuiltInStoreUrl(stores, 'https://www.amazon.com/dp/B001')).toBe(true);
	});

	it('should return false for a custom (editable) store URL', () => {
		expect(isBuiltInStoreUrl(stores, 'https://mystore.com/p/42')).toBe(false);
	});

	it('should return false for an unrecognised domain', () => {
		expect(isBuiltInStoreUrl(stores, 'https://unknown.example/p/1')).toBe(false);
	});
});
