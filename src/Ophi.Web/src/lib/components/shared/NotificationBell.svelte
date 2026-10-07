<script lang="ts">
	import { onMount } from 'svelte';
	import { Bell } from 'lucide-svelte';
	import { api } from '$lib/api/client';
	import { notifications } from '$lib/stores/notifications.svelte';
	import { liveRefresh } from '$lib/utils/liveRefresh';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { formatTimeAgo } from '$lib/format';

	let isOpen = $state(false);
	let loading = $state(false);
	let loadingMore = $state(false);
	let currentPage = $state(1);
	let error = $state<string | null>(null);
	let dropdownEl: HTMLDivElement | undefined = $state();

	async function toggleDropdown() {
		isOpen = !isOpen;
		if (isOpen) {
			await loadNotifications();
		}
	}

	async function loadNotifications() {
		loading = true;
		error = null;
		currentPage = 1;
		try {
			const result = await api.getNotifications({ pageSize: 20 });
			notifications.set(result.items);
			notifications.setTotal(result.total);
		} catch {
			error = 'Failed to load notifications';
		} finally {
			loading = false;
		}
	}

	async function loadMore() {
		loadingMore = true;
		try {
			const nextPage = currentPage + 1;
			const result = await api.getNotifications({ page: nextPage, pageSize: 20 });
			notifications.append(result.items);
			notifications.setTotal(result.total);
			currentPage = nextPage;
		} catch {
			error = 'Failed to load notifications';
		} finally {
			loadingMore = false;
		}
	}

	async function handleNotificationClick(id: string, productId?: string) {
		try {
			await api.markNotificationRead(id);
			notifications.markRead(id);
		} catch {
			// Mark-read is best-effort; don't block navigation
		}
		if (productId) {
			isOpen = false;
			goto(resolve(`/products/${productId}`));
		}
	}

	async function handleMarkAllRead() {
		try {
			await api.markAllNotificationsRead();
			notifications.markAllRead();
		} catch {
			error = 'Failed to mark all as read';
		}
	}

	function handleKeydown(event: KeyboardEvent) {
		if (event.key === 'Escape' && isOpen) {
			isOpen = false;
		}
	}

	function handleClickOutside(event: MouseEvent) {
		if (dropdownEl && !dropdownEl.contains(event.target as Node)) {
			isOpen = false;
		}
	}

	let tabHidden = $state(false);

	function refreshCount() {
		api.getNotificationCount().then((r) => notifications.setUnreadCount(r.unread)).catch(() => {});
	}

	// Live updates: refetch the unread count when a notification is created (or any scrape completes,
	// which is when OOS / back-in-stock notifications are written). The fallback poll is reactive on
	// connection + visibility, so it stops the moment SSE connects and resumes if it drops.
	$effect(() =>
		liveRefresh({
			matches: (evt) => evt.type === 'notification' || evt.type === 'scrape-completed',
			refresh: refreshCount,
			shouldPoll: () => !tabHidden,
			intervalMs: 30000
		})
	);

	onMount(() => {
		// Initial count fetch
		refreshCount();

		function handleVisibility() {
			tabHidden = document.hidden;
			// Refresh immediately when the tab becomes visible again.
			if (!document.hidden) refreshCount();
		}

		document.addEventListener('visibilitychange', handleVisibility);
		document.addEventListener('click', handleClickOutside);

		return () => {
			document.removeEventListener('visibilitychange', handleVisibility);
			document.removeEventListener('click', handleClickOutside);
		};
	});
</script>

<svelte:window onkeydown={handleKeydown} />

<div class="relative" bind:this={dropdownEl} data-notification-bell>
	<button
		onclick={toggleDropdown}
		class="relative p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700 rounded-full transition-colors"
		aria-label="Notifications"
		title="Notifications"
	>
		<Bell size={20} />
		{#if notifications.unreadCount > 0}
			<span
				class="absolute -top-0.5 -right-0.5 flex items-center justify-center min-w-4.5 h-4.5 px-1 text-[10px] font-bold text-white bg-red-500 rounded-full"
				data-testid="unread-badge"
			>
				{notifications.unreadCount > 99 ? '99+' : notifications.unreadCount}
			</span>
		{/if}
	</button>

	{#if isOpen}
		<div
			class="absolute right-0 mt-2 w-80 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg z-50 max-h-96 overflow-hidden"
			role="menu"
			aria-label="Notifications panel"
		>
			<div
				class="flex items-center justify-between px-4 py-3 border-b border-gray-200 dark:border-gray-700"
			>
				<h3 class="text-sm font-semibold text-gray-900 dark:text-white">Notifications</h3>
				{#if notifications.unreadCount > 0}
					<button
						onclick={handleMarkAllRead}
						class="text-xs text-brand dark:text-brand-muted hover:underline"
					>
						Mark all as read
					</button>
				{/if}
			</div>

			{#if error}
				<div class="px-4 py-3 text-center">
					<p role="alert" class="text-xs text-red-500 dark:text-red-400">{error}</p>
					<button
						onclick={loadNotifications}
						class="mt-1 text-xs text-brand dark:text-brand-muted hover:underline"
					>
						Retry
					</button>
				</div>
			{/if}

			<div class="overflow-y-auto max-h-80">
				{#if loading}
					<div class="px-4 py-8 text-center text-sm text-gray-500 dark:text-gray-400">
						Loading...
					</div>
				{:else if !error}
					{#if notifications.items.length === 0}
						<div class="px-4 py-8 text-center text-sm text-gray-500 dark:text-gray-400">
							No notifications
						</div>
					{:else}
						{#each notifications.items as notification (notification.id)}
							<button
								onclick={() =>
									handleNotificationClick(notification.id, notification.productId)}
								class="w-full text-left px-4 py-3 hover:bg-gray-50 dark:hover:bg-gray-700 border-b border-gray-100 dark:border-gray-700 last:border-b-0 transition-colors {notification.isRead ? 'opacity-60' : ''}"
							>
								<div class="flex items-start gap-2">
									<div
										class="mt-1.5 w-2 h-2 rounded-full shrink-0 {notification.isRead ? 'bg-transparent' : 'bg-brand'}"
									></div>
									<div class="flex-1 min-w-0">
										<p
											class="text-sm font-medium text-gray-900 dark:text-white truncate"
										>
											{notification.title}
										</p>
										<p
											class="text-xs text-gray-500 dark:text-gray-400 mt-0.5 line-clamp-2"
										>
											{notification.message}
										</p>
										<p class="text-xs text-gray-400 dark:text-gray-500 mt-1">
											{formatTimeAgo(notification.createdAt)}
										</p>
									</div>
								</div>
							</button>
						{/each}
					{/if}
				{/if}
			</div>

			{#if !loading && !error && notifications.total > notifications.items.length}
				<div class="px-4 py-2 border-t border-gray-200 dark:border-gray-700 text-center">
					<button
						onclick={loadMore}
						disabled={loadingMore}
						class="text-xs text-brand dark:text-brand-muted hover:underline disabled:opacity-50"
					>
						{loadingMore
							? 'Loading...'
							: `Load more (${notifications.items.length} of ${notifications.total})`}
					</button>
				</div>
			{/if}
		</div>
	{/if}
</div>
