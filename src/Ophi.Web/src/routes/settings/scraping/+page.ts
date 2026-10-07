import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) {
		return { settings: null };
	}

	const client = api.withFetch(fetch);
	return { settings: await client.getSettings() };
};
