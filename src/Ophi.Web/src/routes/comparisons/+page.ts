import type { PageLoad } from './$types';
import { api } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	if (!user) return { groups: [], showLandedPrices: false };

	const client = api.withFetch(fetch);
	const [result, settings] = await Promise.all([
		client.getComparisonGroups(),
		// Optional: a failure only leaves landed prices off.
		client.getSettings().catch(() => null)
	]);
	return { groups: result.items, showLandedPrices: settings?.showLandedPrices ?? false };
};
