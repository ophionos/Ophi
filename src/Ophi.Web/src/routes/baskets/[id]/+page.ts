import type { PageLoad } from './$types';
import { api, type Basket, type BasketAlert, type BasketHistory, type ShippingRate } from '$lib/api/client';

export const load: PageLoad = async ({ fetch, parent, params }) => {
	const { user } = await parent();
	if (!user)
		return {
			basket: null as Basket | null,
			shippingRates: [] as ShippingRate[],
			history: null as BasketHistory | null,
			alerts: null as BasketAlert[] | null
		};

	const client = api.withFetch(fetch);
	// A missing basket throws (404) like the comparison detail loader. The rates only tell
	// "free" from "no shipping cost set", so a failed call leaves them empty. A failed history
	// call only hides the chart, and a failed alerts call only hides the alerts.
	const [basket, rates, history, alerts] = await Promise.all([
		client.getBasket(params.id),
		client.listShippingRates().catch(() => ({ items: [] as ShippingRate[] })),
		client.getBasketHistory(params.id).catch(() => null),
		client.listBasketAlerts(params.id).catch(() => null)
	]);
	return {
		basket: basket as Basket | null,
		shippingRates: rates.items,
		history: history as BasketHistory | null,
		alerts: (alerts?.items ?? null) as BasketAlert[] | null
	};
};
