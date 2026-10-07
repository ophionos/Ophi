import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent, params }) => {
	const { user } = await parent();
	if (!user) return { group: null };

	const client = api.withFetch(fetch);
	// The Add Products modal fetches its own (paginated/searchable) product list, so the page
	// only needs the group itself.
	const group = await client.getComparisonGroup(params.id, 7);
	return { group };
};
