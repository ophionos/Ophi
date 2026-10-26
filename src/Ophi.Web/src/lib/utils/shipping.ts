import type { ShippingRate } from '$lib/api/client';

/**
 * The key shipping rates use: the URL's host, lowercase, without a leading "www." — the same
 * normalization the server applies (SiteHost.Normalize). Accepts a full URL or a bare host.
 */
export function shippingHost(urlOrHost: string): string {
	let host = urlOrHost.trim();
	try {
		host = new URL(host).hostname;
	} catch {
		// not a URL: treat it as a bare host
	}
	host = host.toLowerCase();
	return host.startsWith('www.') ? host.slice(4) : host;
}

export function findShippingRate(
	rates: ShippingRate[],
	host: string | null | undefined
): ShippingRate | undefined {
	if (!host) return undefined;
	const key = shippingHost(host);
	return rates.find((r) => r.host === key);
}
