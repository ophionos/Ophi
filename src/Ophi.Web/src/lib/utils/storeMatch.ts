import type { Store } from '$lib/api/client';

/** Extract the hostname from a URL, falling back to the raw string if it can't be parsed. */
export function getHostname(url: string): string {
	try {
		return new URL(url).hostname;
	} catch {
		return url;
	}
}

/**
 * Find the store whose domain patterns match the given hostname.
 *
 * Matches an exact host or any subdomain of a pattern (so `www.amazon.com` matches
 * the `amazon.com` pattern). This is the single matcher shared by the store editor
 * auto-open (stores page) and the product page, so both always agree on which store
 * a URL belongs to — and, crucially, the match is derived purely from the URL's
 * domain, never from `ProductUrl.storeId` (which is only stamped after the first
 * successful scrape and is therefore null for freshly-added/pending products).
 */
export function findStoreForDomain(stores: Store[], domain: string): Store | undefined {
	const host = domain.toLowerCase();
	return stores.find((s) =>
		s.domainPatterns.some((p) => {
			const pattern = p.toLowerCase();
			return host === pattern || host.endsWith(`.${pattern}`);
		})
	);
}

/** True when the given URL belongs to a built-in (non-editable) store. */
export function isBuiltInStoreUrl(stores: Store[], url: string): boolean {
	return findStoreForDomain(stores, getHostname(url))?.isBuiltIn ?? false;
}
