import type { ComparisonGroup } from '$lib/api/client';

let items = $state<ComparisonGroup[]>([]);

export const comparisons = {
	get items() {
		return items;
	},
	set(newItems: ComparisonGroup[]) {
		items = newItems;
	},
	add(group: ComparisonGroup) {
		items = [...items, group];
	},
	update(groupId: string, updatedGroup: ComparisonGroup) {
		items = items.map((g) => (g.id === groupId ? updatedGroup : g));
	},
	remove(groupId: string) {
		items = items.filter((g) => g.id !== groupId);
	},
	clear() {
		items = [];
	}
};
