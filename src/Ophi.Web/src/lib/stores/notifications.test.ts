import { describe, it, expect, beforeEach } from 'vitest';
import { notifications } from './notifications.svelte';
import type { Notification } from '$lib/api/client';

function mockNotification(overrides: Partial<Notification> = {}): Notification {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		title: overrides.title ?? 'Test',
		message: 'Test message',
		type: 'priceAlert',
		isRead: false,
		productId: null,
		productName: null,
		createdAt: new Date().toISOString(),
		...overrides
	} as Notification;
}

describe('notifications store', () => {
	beforeEach(() => {
		notifications.clear();
	});

	it('should start with empty state', () => {
		expect(notifications.items).toEqual([]);
		expect(notifications.unreadCount).toBe(0);
		expect(notifications.total).toBe(0);
	});

	it('should set notifications', () => {
		notifications.set([mockNotification(), mockNotification()]);
		expect(notifications.items).toHaveLength(2);
	});

	it('should set unread count', () => {
		notifications.setUnreadCount(5);
		expect(notifications.unreadCount).toBe(5);
	});

	it('should set total', () => {
		notifications.setTotal(42);
		expect(notifications.total).toBe(42);
	});

	it('should mark a notification as read', () => {
		const n = mockNotification({ id: 'n1', isRead: false });
		notifications.set([n]);
		notifications.setUnreadCount(1);

		notifications.markRead('n1');

		expect(notifications.items[0].isRead).toBe(true);
		expect(notifications.unreadCount).toBe(0);
	});

	it('should not go below zero unread count', () => {
		notifications.setUnreadCount(0);
		notifications.markRead('nonexistent');

		expect(notifications.unreadCount).toBe(0);
	});

	it('should mark all notifications as read', () => {
		notifications.set([
			mockNotification({ id: 'n1', isRead: false }),
			mockNotification({ id: 'n2', isRead: false }),
			mockNotification({ id: 'n3', isRead: true })
		]);
		notifications.setUnreadCount(2);

		notifications.markAllRead();

		expect(notifications.items.every((n) => n.isRead)).toBe(true);
		expect(notifications.unreadCount).toBe(0);
	});

	it('should append a next page of notifications', () => {
		notifications.set([mockNotification({ id: 'n1' }), mockNotification({ id: 'n2' })]);

		notifications.append([mockNotification({ id: 'n3' })]);

		expect(notifications.items.map((n) => n.id)).toEqual(['n1', 'n2', 'n3']);
	});

	it('should dedupe by id when appending (new arrivals shift pagination)', () => {
		notifications.set([mockNotification({ id: 'n1' }), mockNotification({ id: 'n2' })]);

		notifications.append([mockNotification({ id: 'n2' }), mockNotification({ id: 'n3' })]);

		expect(notifications.items.map((n) => n.id)).toEqual(['n1', 'n2', 'n3']);
	});

	it('should clear everything', () => {
		notifications.set([mockNotification()]);
		notifications.setUnreadCount(1);

		notifications.clear();

		expect(notifications.items).toEqual([]);
		expect(notifications.unreadCount).toBe(0);
	});
});
