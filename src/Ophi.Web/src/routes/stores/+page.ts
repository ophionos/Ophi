import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) {
		return { stores: [], shippingRates: [] };
	}

	const client = api.withFetch(fetch);
	const [storesResult, ratesResult] = await Promise.all([
		client.getStores(),
		// Optional section: a failure only leaves the shipping list empty.
		client.listShippingRates().catch(() => ({ items: [] }))
	]);

	return { stores: storesResult.items, shippingRates: ratesResult.items };
};
