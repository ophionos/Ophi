import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import NotificationBell from './NotificationBell.svelte';
import { notifications } from '$lib/stores/notifications.svelte';

// Mock the api client
vi.mock('$lib/api/client', () => ({
	api: {
		getNotificationCount: vi.fn().mockResolvedValue({ unread: 0 }),
		getNotifications: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 }),
		markNotificationRead: vi.fn().mockResolvedValue({}),
		markAllNotificationsRead: vi.fn().mockResolvedValue({})
	}
}));

// Mock navigation
vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

describe('NotificationBell', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		notifications.clear();
	});

	describe('rendering', () => {
		it('should render the bell button', () => {
			render(NotificationBell);
			expect(screen.getByLabelText('Notifications')).toBeInTheDocument();
		});

		it('should render bell icon as SVG', () => {
			const { container } = render(NotificationBell);
			const svg = container.querySelector('svg');
			expect(svg).toBeInTheDocument();
		});

		it('should not show badge when unread count is 0', () => {
			render(NotificationBell);
			expect(screen.queryByTestId('unread-badge')).not.toBeInTheDocument();
		});

		it('should show badge when unread count is greater than 0', () => {
			notifications.setUnreadCount(3);
			render(NotificationBell);
			const badge = screen.getByTestId('unread-badge');
			expect(badge).toBeInTheDocument();
			expect(badge.textContent?.trim()).toBe('3');
		});

		it('should show 99+ when unread count exceeds 99', () => {
			notifications.setUnreadCount(150);
			render(NotificationBell);
			const badge = screen.getByTestId('unread-badge');
			expect(badge.textContent?.trim()).toBe('99+');
		});
	});

	describe('dropdown', () => {
		it('should not show dropdown initially', () => {
			render(NotificationBell);
			expect(screen.queryByRole('menu')).not.toBeInTheDocument();
		});

		it('should show dropdown on click', async () => {
			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);
			expect(screen.getByRole('menu')).toBeInTheDocument();
		});

		it('should toggle dropdown on click', async () => {
			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');

			await fireEvent.click(button);
			expect(screen.getByRole('menu')).toBeInTheDocument();

			await fireEvent.click(button);
			expect(screen.queryByRole('menu')).not.toBeInTheDocument();
		});

		it('should show empty state when no notifications', async () => {
			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);

			// Wait for loading to finish
			await vi.waitFor(() => {
				expect(screen.getByText('No notifications')).toBeInTheDocument();
			});
		});

		it('should announce the error when notifications fail to load', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getNotifications).mockRejectedValueOnce(new Error('offline'));
			render(NotificationBell);
			await fireEvent.click(screen.getByLabelText('Notifications'));

			await vi.waitFor(() => {
				expect(screen.getByRole('alert')).toHaveTextContent('Failed to load notifications');
			});
		});

		it('should show notification list', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getNotifications).mockResolvedValueOnce({
				items: [
					{
						id: '1',
						title: 'Price dropped',
						message: 'Widget is now $45',
						type: 'priceAlert' as const,
						isRead: false,
						productId: 'p1',
						productName: 'Widget',
						createdAt: new Date().toISOString()
					}
				],
				total: 1,
				page: 1,
				pageSize: 20
			});

			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);

			await vi.waitFor(() => {
				expect(screen.getByText('Price dropped')).toBeInTheDocument();
				expect(screen.getByText('Widget is now $45')).toBeInTheDocument();
			});
		});

		it('should show mark all as read button when there are unread notifications', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getNotificationCount).mockResolvedValue({ unread: 2 });

			render(NotificationBell);

			await vi.waitFor(() => {
				expect(screen.getByTestId('unread-badge')).toBeInTheDocument();
			});

			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);

			await vi.waitFor(() => {
				expect(screen.getByText('Mark all as read')).toBeInTheDocument();
			});
		});

		it('should not show mark all as read when no unread', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getNotificationCount).mockResolvedValue({ unread: 0 });

			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);

			await vi.waitFor(() => {
				expect(screen.getByText('No notifications')).toBeInTheDocument();
			});
			expect(screen.queryByText('Mark all as read')).not.toBeInTheDocument();
		});

		it('should close on Escape key', async () => {
			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);
			expect(screen.getByRole('menu')).toBeInTheDocument();

			await fireEvent.keyDown(window, { key: 'Escape' });
			expect(screen.queryByRole('menu')).not.toBeInTheDocument();
		});
	});

	describe('load more', () => {
		function makeNotifications(count: number, offset = 0) {
			return Array.from({ length: count }, (_, i) => ({
				id: `n${offset + i}`,
				title: `Notification ${offset + i}`,
				message: 'msg',
				type: 'priceAlert' as const,
				isRead: true,
				productId: undefined,
				productName: undefined,
				createdAt: new Date().toISOString()
			}));
		}

		it('should load the next page when "Load more" is clicked', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getNotifications)
				.mockResolvedValueOnce({ items: makeNotifications(20), total: 25, page: 1, pageSize: 20 })
				.mockResolvedValueOnce({ items: makeNotifications(5, 20), total: 25, page: 2, pageSize: 20 });

			render(NotificationBell);
			await fireEvent.click(screen.getByLabelText('Notifications'));

			const loadMore = await screen.findByRole('button', { name: /load more/i });
			await fireEvent.click(loadMore);

			expect(api.getNotifications).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 });
			expect(await screen.findByText('Notification 24')).toBeInTheDocument();
			// Everything is loaded now, so the button disappears.
			expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
		});

		it('should not show "Load more" when all notifications are loaded', async () => {
			const { api } = await import('$lib/api/client');
			vi.mocked(api.getNotifications).mockResolvedValueOnce({
				items: makeNotifications(3),
				total: 3,
				page: 1,
				pageSize: 20
			});

			render(NotificationBell);
			await fireEvent.click(screen.getByLabelText('Notifications'));

			expect(await screen.findByText('Notification 0')).toBeInTheDocument();
			expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
		});
	});

	describe('accessibility', () => {
		it('should have aria-label on bell button', () => {
			render(NotificationBell);
			expect(screen.getByLabelText('Notifications')).toBeInTheDocument();
		});

		it('should have title attribute on bell button', () => {
			render(NotificationBell);
			expect(screen.getByTitle('Notifications')).toBeInTheDocument();
		});

		it('should have aria-label on notifications dropdown', async () => {
			render(NotificationBell);
			const button = screen.getByLabelText('Notifications');
			await fireEvent.click(button);

			const menu = screen.getByRole('menu');
			expect(menu).toHaveAttribute('aria-label', 'Notifications panel');
		});
	});
});
