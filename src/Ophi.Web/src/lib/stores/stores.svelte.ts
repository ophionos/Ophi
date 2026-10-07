import type { Store } from '$lib/api/client';

let items = $state<Store[]>([]);

export const stores = {
	get items() {
		return items;
	},
	set(newItems: Store[]) {
		items = newItems;
	},
	add(store: Store) {
		items = [...items, store];
	},
	update(storeId: string, updatedStore: Store) {
		items = items.map((s) => (s.storeId === storeId ? updatedStore : s));
	},
	remove(storeId: string) {
		items = items.filter((s) => s.storeId !== storeId);
	},
	clear() {
		items = [];
	}
};
