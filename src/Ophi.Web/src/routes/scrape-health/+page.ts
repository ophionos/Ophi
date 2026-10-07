import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) return { health: null };

	const client = api.withFetch(fetch);
	const health = await client.getScrapeHealth();
	return { health };
};
