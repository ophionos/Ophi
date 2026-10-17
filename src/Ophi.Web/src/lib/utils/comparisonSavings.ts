import type { ComparisonGroupDetail } from '$lib/api/client';

/**
 * How much the group's best-priced product saves against the next cheapest one, for the
 * comparison spotlight. Null when there is nothing to compare against.
 *
 * Starts from `bestPriceProductId`, which the backend picks within the dominant currency
 * (`ProductPriceAggregator.DominantCurrency`), and compares only products in that currency.
 * Subtracting prices in different currencies gives a meaningless number, and the lowest raw
 * number in the group can be in a minority currency.
 */
export function savingsVsNext(group: ComparisonGroupDetail | null): number | null {
	const best = group?.products.find((p) => p.id === group.bestPriceProductId);
	if (!group || best?.currentPrice == null) return null;
	const currency = best.currency.toUpperCase();
	const others = group.products
		.filter(
			(p) => p.id !== best.id && p.currentPrice != null && p.currency.toUpperCase() === currency
		)
		.map((p) => p.currentPrice!);
	if (others.length === 0) return null;
	return Math.min(...others) - best.currentPrice;
}
