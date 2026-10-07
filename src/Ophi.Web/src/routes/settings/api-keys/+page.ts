import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) {
		return { apiKeys: [] };
	}

	const client = api.withFetch(fetch);
	return { apiKeys: await client.listApiKeys() };
};
