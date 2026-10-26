import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) return { baskets: [] };

	const result = await api.withFetch(fetch).listBaskets();
	return { baskets: result.items };
};
