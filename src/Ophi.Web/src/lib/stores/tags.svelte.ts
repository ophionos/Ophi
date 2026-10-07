import type { Tag } from '$lib/api/client';

let items = $state<Tag[]>([]);

export const tags = {
	get items() {
		return items;
	},
	set(newItems: Tag[]) {
		items = newItems;
	},
	add(tag: Tag) {
		items = [...items, tag];
	},
	remove(id: string) {
		items = items.filter((t) => t.id !== id);
	},
	update(id: string, updated: Partial<Tag>) {
		items = items.map((t) => (t.id === id ? { ...t, ...updated } : t));
	},
	clear() {
		items = [];
	}
};
