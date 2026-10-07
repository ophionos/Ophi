import type { Product } from '$lib/api/client';

let items = $state<Product[]>([]);
let total = $state(0);
let currentPage = $state(1);
let currentPageSize = $state(24);

export const products = {
	get items() {
		return items;
	},
	get total() {
		return total;
	},
	get page() {
		return currentPage;
	},
	get pageSize() {
		return currentPageSize;
	},
	set(newItems: Product[]) {
		items = newItems;
	},
	setPage(newItems: Product[], newTotal: number, page: number, pageSize: number) {
		items = newItems;
		total = newTotal;
		currentPage = page;
		currentPageSize = pageSize;
	},
	add(product: Product) {
		items = [...items, product];
	},
	remove(id: string) {
		items = items.filter((p) => p.id !== id);
	},
	update(id: string, updated: Partial<Product>) {
		items = items.map((p) => (p.id === id ? { ...p, ...updated } : p));
	},
	clear() {
		items = [];
	}
};
