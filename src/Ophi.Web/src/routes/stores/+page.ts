import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) {
		return { stores: [] };
	}

	const client = api.withFetch(fetch);
	const storesResult = await client.getStores();

	return { stores: storesResult.items };
};
