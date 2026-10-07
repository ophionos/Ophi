export function isValidHttpUrl(value: string): boolean {
	try {
		const url = new URL(value);
		return url.protocol === 'http:' || url.protocol === 'https:';
	} catch {
		return false;
	}
}

// One formatter per currency; Intl.NumberFormat construction is expensive relative to format().
const priceFormatters = new Map<string, Intl.NumberFormat | null>();

/**
 * Locale-aware price formatting: "$189.00" / "€17.91" / "¥1,200" instead of "USD 189.00".
 * Pinned to en-US so SSR and client output are identical (no hydration flicker) and tests are
 * machine-independent. Non-ISO currency strings fall back to "<code> <amount>".
 */
export function formatPrice(amount: number, currency: string): string {
	const fallback = `${currency ? `${currency} ` : ''}${amount.toFixed(2)}`;
	if (!/^[A-Za-z]{3}$/.test(currency)) return fallback;

	let formatter = priceFormatters.get(currency);
	if (formatter === undefined) {
		try {
			formatter = new Intl.NumberFormat('en-US', { style: 'currency', currency });
		} catch {
			formatter = null; // malformed code — remember the failure, keep falling back
		}
		priceFormatters.set(currency, formatter);
	}
	return formatter ? formatter.format(amount) : fallback;
}

export function formatCheckInterval(minutes: number): string {
	if (minutes >= 60 && minutes % 60 === 0) return `${minutes / 60}h`;
	if (minutes > 60) return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
	return `${minutes}m`;
}

export const CHECK_INTERVAL_OPTIONS = [
	{ value: '', label: 'Default' },
	{ value: '15', label: '15 minutes' },
	{ value: '30', label: '30 minutes' },
	{ value: '60', label: '1 hour' },
	{ value: '120', label: '2 hours' },
	{ value: '240', label: '4 hours' },
	{ value: '360', label: '6 hours' },
	{ value: '720', label: '12 hours' },
	{ value: '1440', label: '24 hours' }
];

export function formatTimeAgo(dateStr: string): string {
	const date = new Date(dateStr);
	const now = new Date();
	const seconds = Math.floor((now.getTime() - date.getTime()) / 1000);

	if (seconds < 60) return 'just now';
	if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
	if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
	if (seconds < 604800) return `${Math.floor(seconds / 86400)}d ago`;
	return date.toLocaleDateString();
}
