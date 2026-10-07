import type { PageLoad } from './$types';
import { api, type GetProductsParams, type ProductStatus } from '$lib/api/client';

const EMPTY_PAGE = {
	items: [],
	total: 0,
	page: 1,
	pageSize: 24,
	atLowestCount: 0,
	priceDropCount: 0,
	withAlertsCount: 0
};

/**
 * The dashboard's filters live in the URL (written shallowly by the page via replaceState), so a
 * refresh, a shared link, or a bookmark lands on the same filtered view. This loader is the only
 * place that turns those params into an API query for the initial load; subsequent filter changes
 * refetch client-side without re-running it.
 */
function parseProductParams(searchParams: URLSearchParams): GetProductsParams {
	const statsFilter = searchParams.get('filter');
	const sortBy = searchParams.get('sortBy') ?? undefined;
	const pageNum = parseInt(searchParams.get('page') ?? '', 10);

	return {
		tagId: searchParams.get('tag') ?? undefined,
		search: searchParams.get('search') ?? undefined,
		status: (searchParams.get('status') as ProductStatus) || undefined,
		sortBy,
		sortDirection: sortBy ? (searchParams.get('sortDirection') ?? undefined) : undefined,
		page: Number.isFinite(pageNum) && pageNum > 1 ? pageNum : undefined,
		includeSparkline: true,
		atLowest: statsFilter === 'lowest' || undefined,
		favourite: searchParams.get('favourite') === 'true' || undefined,
		priceDrop: statsFilter === 'drops' || undefined,
		hasAlerts: statsFilter === 'alerts' || undefined
	};
}

export const load: PageLoad = async ({ fetch, parent, url }) => {
	const { user } = await parent();
	if (!user) {
		return { products: EMPTY_PAGE, tags: [] };
	}

	const client = api.withFetch(fetch);
	const [products, tagsResult] = await Promise.all([
		client.getProducts(parseProductParams(url.searchParams)),
		client.getTags()
	]);

	return {
		products,
		tags: tagsResult.items
	};
};
