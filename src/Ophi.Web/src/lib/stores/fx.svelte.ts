import { api } from '$lib/api/client';

/**
 * Display-currency state: the user's preference plus the ECB rates to convert with. Display only
 * — see `$lib/utils/fx`.
 */
let displayCurrency = $state<string | null>(null);
let rates = $state<Record<string, number>>({});
let asOf = $state<string | null>(null);
let loading: Promise<void> | null = null;

export const fx = {
	get displayCurrency() {
		return displayCurrency;
	},
	get rates() {
		return rates;
	},
	get asOf() {
		return asOf;
	},

	/**
	 * Loads the preference and, only when one is set, the rates. Idempotent per session: concurrent
	 * and repeated calls share one request pair. Failures leave conversion off, never block the page.
	 */
	load(): Promise<void> {
		loading ??= (async () => {
			try {
				const settings = await api.getSettings();
				displayCurrency = settings.displayCurrency ?? null;
				if (displayCurrency) await this.loadRates();
			} catch {
				displayCurrency = null;
			}
		})();
		return loading;
	},

	async loadRates() {
		const result = await api.getFxRates();
		rates = result.rates;
		asOf = result.asOf ?? null;
	},

	/** After the user changes the setting. */
	async setDisplayCurrency(currency: string | null) {
		displayCurrency = currency;
		if (currency && Object.keys(rates).length === 0) {
			try {
				await this.loadRates();
			} catch {
				// conversion simply stays hidden until rates exist
			}
		}
	},

	/** Test/sign-out helper. */
	set(state: { displayCurrency: string | null; rates: Record<string, number>; asOf: string | null }) {
		displayCurrency = state.displayCurrency;
		rates = state.rates;
		asOf = state.asOf;
	},

	reset() {
		displayCurrency = null;
		rates = {};
		asOf = null;
		loading = null;
	}
};
