import { api, ApiError } from '$lib/api/client';

export class QuickAlert {
	open = $state(false);
	price = $state(0);
	loading = $state(false);
	success = $state(false);
	error = $state('');
	isNetworkError = $state(false);

	#timer: ReturnType<typeof setTimeout> | undefined;
	#onCreated: (() => void) | undefined;

	constructor(onCreated?: () => void) {
		this.#onCreated = onCreated;
	}

	toggle(currentPrice: number | null | undefined): void {
		if (this.open) {
			this.#cancelTimer();
			this.open = false;
			this.loading = false;
			this.success = false;
			this.error = '';
			this.isNetworkError = false;
			return;
		}
		this.price = currentPrice ? Math.floor(currentPrice * 0.9 * 100) / 100 : 0;
		this.error = '';
		this.success = false;
		this.open = true;
	}

	async submit(productId: string): Promise<void> {
		this.loading = true;
		this.error = '';
		this.isNetworkError = false;
		try {
			await api.createAlert(productId, this.price, 'below');
			this.success = true;
			this.loading = false;
			this.#onCreated?.();
			this.#timer = setTimeout(() => {
				this.open = false;
				this.success = false;
			}, 2000);
		} catch (e: unknown) {
			if (e instanceof ApiError && e.hasFieldErrors()) {
				this.error = e.getFieldErrors('targetprice')[0] ?? e.message;
			} else if (e instanceof ApiError && e.code !== 'UnknownError') {
				this.error = e.message;
			} else {
				this.error = e instanceof Error ? e.message : 'Failed to create alert';
				this.isNetworkError = true;
			}
			this.loading = false;
		}
	}

	destroy(): void {
		this.#cancelTimer();
	}

	#cancelTimer(): void {
		if (this.#timer !== undefined) {
			clearTimeout(this.#timer);
			this.#timer = undefined;
		}
	}
}
