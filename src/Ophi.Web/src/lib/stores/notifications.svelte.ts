import type { Notification } from '$lib/api/client';

let items = $state<Notification[]>([]);
let unreadCount = $state(0);
let total = $state(0);

export const notifications = {
	get items() {
		return items;
	},
	get unreadCount() {
		return unreadCount;
	},
	get total() {
		return total;
	},
	set(newItems: Notification[]) {
		items = newItems;
	},
	/** Append a next page, deduping by id — new arrivals shift offset pagination. */
	append(newItems: Notification[]) {
		// eslint-disable-next-line svelte/prefer-svelte-reactivity -- local dedupe lookup, never rendered
		const existing = new Set(items.map((n) => n.id));
		items = [...items, ...newItems.filter((n) => !existing.has(n.id))];
	},
	setUnreadCount(count: number) {
		unreadCount = count;
	},
	setTotal(count: number) {
		total = count;
	},
	markRead(id: string) {
		items = items.map((n) => (n.id === id ? { ...n, isRead: true } : n));
		unreadCount = Math.max(0, unreadCount - 1);
	},
	markAllRead() {
		items = items.map((n) => ({ ...n, isRead: true }));
		unreadCount = 0;
	},
	clear() {
		items = [];
		unreadCount = 0;
	}
};
