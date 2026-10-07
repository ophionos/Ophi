import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) return { tags: [] };

	const client = api.withFetch(fetch);
	const result = await client.getTags();
	return { tags: result.items };
};
