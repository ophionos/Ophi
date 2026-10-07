import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) return { groups: [] };

	const client = api.withFetch(fetch);
	const result = await client.getComparisonGroups();
	return { groups: result.items };
};
