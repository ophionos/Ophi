/**
 * Display-currency conversion. Display only: nothing here may feed an alert target, a comparison or
 * any stored value — the backend deliberately keeps rates out of those paths (docs/agent-notes.md).
 *
 * Rates are the ECB's: units of each currency per 1 EUR, so A→B is `amount / rate[A] * rate[B]`.
 */

/** Longer than any ECB publication gap (weekend + public holidays). */
const STALE_AFTER_MS = 4 * 24 * 60 * 60 * 1000;

export function convert(
	amount: number,
	from: string,
	to: string,
	rates: Readonly<Record<string, number>>
): number | null {
	const f = from.toUpperCase();
	const t = to.toUpperCase();
	if (f === t) return null;
	const fromRate = rates[f];
	const toRate = rates[t];
	// A currency the ECB doesn't publish gets no conversion — never a guess.
	if (!fromRate || !toRate) return null;
	return (amount / fromRate) * toRate;
}

export function isStale(asOf: string, now: Date = new Date()): boolean {
	return now.getTime() - Date.parse(asOf) > STALE_AFTER_MS;
}
