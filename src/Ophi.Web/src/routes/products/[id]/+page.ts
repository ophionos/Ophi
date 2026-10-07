import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent, params }) => {
	const { user } = await parent();
	if (!user) {
		return { product: null, priceHistory: null, comparisonGroups: [], tags: [], stores: [] };
	}

	const client = api.withFetch(fetch);
	const [product, priceHistory, groups, tagsResult, storesResult] = await Promise.all([
		client.getProduct(params.id),
		client.getPriceHistory(params.id, 7),
		client.getComparisonGroups(),
		client.getTags(),
		client.getStores()
	]);

	return {
		product,
		priceHistory,
		comparisonGroups: groups.items,
		tags: tagsResult.items,
		// Used to hide the "Store configuration" gear on built-in store URLs (those stores
		// aren't editable). Matched by domain so it works before a product's first scrape.
		stores: storesResult.items
	};
};
