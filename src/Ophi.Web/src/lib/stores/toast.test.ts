import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { toast } from './toast.svelte';

describe('toast store', () => {
	beforeEach(() => {
		vi.useFakeTimers();
		// Clear any existing toasts
		toast.items.forEach((t) => toast.dismiss(t.id));
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('should start with empty items', () => {
		expect(toast.items).toEqual([]);
	});

	it('should add a success toast', () => {
		toast.success('Operation successful');

		expect(toast.items).toHaveLength(1);
		expect(toast.items[0].message).toBe('Operation successful');
		expect(toast.items[0].type).toBe('success');
	});

	it('should add an error toast', () => {
		toast.error('Something went wrong');

		expect(toast.items).toHaveLength(1);
		expect(toast.items[0].message).toBe('Something went wrong');
		expect(toast.items[0].type).toBe('error');
	});

	it('should add an info toast', () => {
		toast.info('Heads up');

		expect(toast.items).toHaveLength(1);
		expect(toast.items[0].type).toBe('info');
	});

	it('should assign unique ids to each toast', () => {
		toast.success('First');
		toast.error('Second');

		expect(toast.items).toHaveLength(2);
		expect(toast.items[0].id).not.toBe(toast.items[1].id);
	});

	it('should dismiss a toast by id', () => {
		toast.success('Dismissable');
		const id = toast.items[0].id;

		toast.dismiss(id);

		expect(toast.items).toHaveLength(0);
	});

	it('should auto-dismiss after timeout', () => {
		toast.success('Auto dismiss');

		expect(toast.items).toHaveLength(1);

		vi.advanceTimersByTime(3500);

		expect(toast.items).toHaveLength(0);
	});

	it('should handle multiple toasts with staggered auto-dismiss', () => {
		toast.success('First');
		vi.advanceTimersByTime(1000);
		toast.error('Second');

		expect(toast.items).toHaveLength(2);

		vi.advanceTimersByTime(2500); // 3500ms from first, 2500ms from second
		expect(toast.items).toHaveLength(1);
		expect(toast.items[0].message).toBe('Second');

		vi.advanceTimersByTime(1000); // 3500ms from second
		expect(toast.items).toHaveLength(0);
	});
});
