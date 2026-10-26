import type { PageLoad } from './$types';
import { api, type ShippingRate } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent, params }) => {
	const { user } = await parent();
	if (!user) return { group: null, showLandedPrices: false, shippingRates: [] };

	const client = api.withFetch(fetch);
	// The Add Products modal fetches its own (paginated/searchable) product list, so the page
	// only needs the group itself.
	const group = await client.getComparisonGroup(params.id, 7);
	// Landed prices are optional: a failed settings call only leaves them off. The rates tell
	// "free shipping" from "no shipping cost set" when a product's shipping is 0.
	const settings = await client.getSettings().catch(() => null);
	const showLandedPrices = settings?.showLandedPrices ?? false;
	const shippingRates: ShippingRate[] = showLandedPrices
		? await client
				.listShippingRates()
				.then((r) => r.items)
				.catch(() => [])
		: [];
	return { group, showLandedPrices, shippingRates };
};
