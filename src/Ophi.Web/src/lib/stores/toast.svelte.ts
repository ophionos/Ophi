export type ToastType = 'success' | 'error' | 'info';

export interface Toast {
	id: string;
	message: string;
	type: ToastType;
}

const DURATION_MS = 3500;

function createToastStore() {
	let items = $state<Toast[]>([]);
	let nextId = 0;

	function add(message: string, type: ToastType = 'info') {
		const id = `toast-${++nextId}`;
		items = [...items, { id, message, type }];
		setTimeout(() => dismiss(id), DURATION_MS);
	}

	function dismiss(id: string) {
		items = items.filter((t) => t.id !== id);
	}

	return {
		get items() {
			return items;
		},
		success: (message: string) => add(message, 'success'),
		error: (message: string) => add(message, 'error'),
		info: (message: string) => add(message, 'info'),
		dismiss
	};
}

export const toast = createToastStore();
