import type { PageLoad } from './$types';
import { api, type GetProductsParams, type ProductStatus } from '$lib/api/client';

const EMPTY_PAGE = {
	items: [],
	total: 0,
	page: 1,
	pageSize: 24,
	atLowestCount: 0,
	priceDropCount: 0,
	withAlertsCount: 0,
	needsActionCount: 0,
	nearTargetCount: 0,
	productCount: 0,
	urlCount: 0,
	stores: []
};

/** How many products the action card lists before it links to the filtered table. */
const ACTION_CARD_SIZE = 10;

/**
 * The dashboard's filters live in the URL (written shallowly by the page via replaceState), so a
 * refresh, a shared link, or a bookmark lands on the same filtered view. This loader is the only
 * place that turns those params into an API query for the initial load; subsequent filter changes
 * refetch client-side without re-running it.
 */
function parseProductParams(searchParams: URLSearchParams): GetProductsParams {
	const opportunity = searchParams.get('filter');
	const sortBy = searchParams.get('sortBy') ?? undefined;
	const pageNum = parseInt(searchParams.get('page') ?? '', 10);

	return {
		tagId: searchParams.get('tag') ?? undefined,
		storeId: searchParams.get('store') ?? undefined,
		search: searchParams.get('search') ?? undefined,
		status: (searchParams.get('status') as ProductStatus) || undefined,
		sortBy,
		sortDirection: sortBy ? (searchParams.get('sortDirection') ?? undefined) : undefined,
		page: Number.isFinite(pageNum) && pageNum > 1 ? pageNum : undefined,
		includeSparkline: true,
		atLowest: opportunity === 'lowest' || undefined,
		nearTarget: opportunity === 'near' || undefined,
		needsAction: opportunity === 'action' || undefined,
		favourite: searchParams.get('favourite') === 'true' || undefined
	};
}

export const load: PageLoad = async ({ fetch, parent, url }) => {
	const { user } = await parent();
	if (!user) {
		return {
			products: EMPTY_PAGE,
			tags: [],
			actionProducts: EMPTY_PAGE,
			challengeAvailable: false
		};
	}

	const client = api.withFetch(fetch);
	// The action card has its own query: the products that need the user stay visible whatever
	// the table is filtered to.
	const [products, tagsResult, actionProducts, challenge] = await Promise.all([
		client.getProducts(parseProductParams(url.searchParams)),
		client.getTags(),
		client.getProducts({ needsAction: true, pageSize: ACTION_CARD_SIZE }),
		client.getChallengeAvailability().catch(() => ({ available: false }))
	]);

	return {
		products,
		tags: tagsResult.items,
		actionProducts,
		challengeAvailable: challenge.available
	};
};
