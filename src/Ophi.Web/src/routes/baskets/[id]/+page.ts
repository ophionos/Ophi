import type { PageLoad } from './$types';
import { api, type Basket, type ShippingRate } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent, params }) => {
	const { user } = await parent();
	if (!user) return { basket: null as Basket | null, shippingRates: [] as ShippingRate[] };

	const client = api.withFetch(fetch);
	// A missing basket throws (404) like the comparison detail loader. The rates only tell
	// "free" from "no shipping cost set", so a failed call leaves them empty.
	const [basket, rates] = await Promise.all([
		client.getBasket(params.id),
		client.listShippingRates().catch(() => ({ items: [] as ShippingRate[] }))
	]);
	return { basket: basket as Basket | null, shippingRates: rates.items };
};
