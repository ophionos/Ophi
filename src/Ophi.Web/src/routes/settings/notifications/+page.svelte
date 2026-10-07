<script lang="ts">
	import type { PageData } from './$types';
	import {
		api,
		type WebhookTarget,
		type CreateWebhookTargetRequest,
		type UpdateWebhookTargetRequest
	} from '$lib/api/client';
	import WebhookModal from '$lib/components/webhooks/WebhookModal.svelte';
	import WebhookList from '$lib/components/webhooks/WebhookList.svelte';
	import ConfirmModal from '$lib/components/shared/ConfirmModal.svelte';
	import PushChannelCard from '$lib/components/settings/PushChannelCard.svelte';
	import { toast } from '$lib/stores/toast.svelte';

	let { data: pageData }: { data: PageData } = $props();

	let discordWebhookUrl = $state('');
	let discordWebhookConfigured = $state(false);
	let discordNotificationsEnabled = $state(false);
	let discordLoading = $state(false);
	let discordTestLoading = $state(false);

	let emailTestLoading = $state(false);
	let emailNotificationsEnabled = $state(true);
	let emailLoading = $state(false);

	// Email is server-level configuration, so there is nothing to edit here — only the address
	// alerts land on and whether the server can actually send. A silently dead SMTP setup used to
	// be undiscoverable until a price alert failed to arrive.
	const emailConfigured = $derived(pageData.settings?.emailConfigured ?? false);
	const alertEmail = $derived(pageData.email ?? '');

	let webhookTargets = $state<WebhookTarget[]>([]);
	let webhookModalOpen = $state(false);
	let editingWebhook = $state<WebhookTarget | undefined>(undefined);
	let webhookDeleteConfirmOpen = $state(false);
	let deletingWebhookId = $state<string | undefined>(undefined);
	let webhookDeleteLoading = $state(false);
	let webhookTestingId = $state<string | undefined>(undefined);

	$effect(() => {
		webhookTargets = pageData.webhooks;
		const settings = pageData.settings;
		if (settings) {
			discordWebhookConfigured = settings.discordWebhookConfigured;
			discordNotificationsEnabled = settings.discordNotificationsEnabled;
			emailNotificationsEnabled = settings.emailNotificationsEnabled;
		}
	});

	async function handleToggleDiscordNotifications() {
		discordLoading = true;
		const newValue = !discordNotificationsEnabled;
		try {
			await api.updateSettings({ discordNotificationsEnabled: newValue });
			discordNotificationsEnabled = newValue;
			toast.success(newValue ? 'Discord notifications enabled' : 'Discord notifications disabled');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
		} finally {
			discordLoading = false;
		}
	}

	async function handleDiscordWebhookChange(e: Event) {
		const input = e.target as HTMLInputElement;
		const newValue = input.value.trim();
		discordLoading = true;
		try {
			await api.updateSettings({ discordWebhookUrl: newValue });
			discordWebhookConfigured = !!newValue;
			discordWebhookUrl = '';
			input.value = '';
			toast.success(newValue ? 'Discord webhook URL saved' : 'Discord webhook URL cleared');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
			input.value = discordWebhookUrl;
		} finally {
			discordLoading = false;
		}
	}

	async function handleToggleEmailNotifications() {
		emailLoading = true;
		const newValue = !emailNotificationsEnabled;
		try {
			await api.updateSettings({ emailNotificationsEnabled: newValue });
			emailNotificationsEnabled = newValue;
			toast.success(newValue ? 'Email notifications enabled' : 'Email notifications disabled');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
		} finally {
			emailLoading = false;
		}
	}

	async function handleSendTestEmail() {
		emailTestLoading = true;
		try {
			const result = await api.sendTestEmail();
			if (result.success) {
				toast.success(`Test email sent to ${result.sentTo ?? alertEmail}`);
			} else {
				// The SMTP error text is the diagnostic a self-hoster needs, so surface it verbatim.
				toast.error(result.error ?? 'Failed to send test email');
			}
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to send test email');
		} finally {
			emailTestLoading = false;
		}
	}

	async function handleTestDiscordWebhook() {
		discordTestLoading = true;
		try {
			const result = await api.testDiscordWebhook();
			if (result.success) {
				toast.success('Test message sent to Discord!');
			} else {
				toast.error(result.error ?? 'Failed to send test message');
			}
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to send test message');
		} finally {
			discordTestLoading = false;
		}
	}

	async function handleSaveWebhook(data: CreateWebhookTargetRequest | UpdateWebhookTargetRequest) {
		if (editingWebhook) {
			const updated = await api.updateWebhookTarget(editingWebhook.id, data as UpdateWebhookTargetRequest);
			webhookTargets = webhookTargets.map((w) => (w.id === updated.id ? updated : w));
			toast.success(`Webhook "${updated.name}" updated`);
		} else {
			const created = await api.createWebhookTarget(data as CreateWebhookTargetRequest);
			webhookTargets = [...webhookTargets, created];
			toast.success(`Webhook "${created.name}" added`);
		}
		webhookModalOpen = false;
		editingWebhook = undefined;
	}

	function handleDeleteWebhook(id: string) {
		deletingWebhookId = id;
		webhookDeleteConfirmOpen = true;
	}

	async function handleConfirmDeleteWebhook() {
		if (!deletingWebhookId) return;
		webhookDeleteLoading = true;
		try {
			await api.deleteWebhookTarget(deletingWebhookId);
			webhookTargets = webhookTargets.filter((w) => w.id !== deletingWebhookId);
			toast.success('Webhook deleted');
			webhookDeleteConfirmOpen = false;
			deletingWebhookId = undefined;
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to delete webhook');
		} finally {
			webhookDeleteLoading = false;
		}
	}

	async function handleTestWebhook(id: string) {
		webhookTestingId = id;
		try {
			const result = await api.testWebhookTarget(id);
			if (result.success) {
				toast.success('Test payload sent successfully!');
			} else {
				toast.error(result.error ?? 'Webhook returned an error');
			}
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to send test');
		} finally {
			webhookTestingId = undefined;
		}
	}
</script>

<svelte:head>
	<title>Notification Settings - Ophi</title>
</svelte:head>

<div class="space-y-3">
	<!-- Email — the primary alert channel. Read-only: SMTP is server configuration. -->
	<div
		class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3"
	>
		<div class="flex items-center justify-between gap-3">
			<div class="min-w-0">
				<p class="text-sm font-medium text-gray-900 dark:text-white">Email Notifications</p>
				<p class="text-xs text-gray-500 dark:text-gray-400">
					Price alerts are sent to
					<span class="font-medium text-gray-700 dark:text-gray-300" data-testid="email-address"
						>{alertEmail}</span
					>
				</p>
			</div>
			<div class="flex items-center gap-3 shrink-0">
				<!-- Stays usable while notifications are off: it diagnoses SMTP, it is not a delivery. -->
				<button
					onclick={handleSendTestEmail}
					disabled={!emailConfigured || emailTestLoading}
					class="px-3 py-1.5 text-xs font-medium rounded-md border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
					data-testid="email-test-button"
				>
					{emailTestLoading ? 'Sending…' : 'Send test email'}
				</button>
				<button
					onclick={handleToggleEmailNotifications}
					disabled={emailLoading}
					class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors {emailNotificationsEnabled ? 'bg-brand' : 'bg-gray-300 dark:bg-gray-600'} disabled:opacity-50"
					role="switch"
					aria-checked={emailNotificationsEnabled}
					data-testid="email-toggle"
					aria-label="Toggle email notifications"
				>
					<span
						class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {emailNotificationsEnabled ? 'translate-x-6' : 'translate-x-1'}"
					></span>
				</button>
			</div>
		</div>
		{#if !emailNotificationsEnabled}
			<p class="text-xs text-gray-500 dark:text-gray-400" data-testid="email-status">
				Email notifications are turned off for this account. Price alerts still appear in the app
				and still reach your other channels.
			</p>
		{:else if emailConfigured}
			<p class="text-xs text-green-600 dark:text-green-400" data-testid="email-status">
				SMTP is configured on this server. Send a test email to confirm it actually delivers.
			</p>
		{:else}
			<p class="text-xs text-amber-600 dark:text-amber-400" data-testid="email-status">
				SMTP is not configured on this server, so price alert emails will not be delivered. Set
				SMTP_HOST, SMTP_USER, SMTP_PASS and SMTP_FROM, then restart.
			</p>
		{/if}
	</div>

	<!-- Discord notifications -->
	<div class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3">
		<div class="flex items-center justify-between">
			<div>
				<p class="text-sm font-medium text-gray-900 dark:text-white">Discord Notifications</p>
				<p class="text-xs text-gray-500 dark:text-gray-400">
					Send price alert notifications to a Discord channel
				</p>
			</div>
			<button
				onclick={handleToggleDiscordNotifications}
				disabled={discordLoading}
				class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors {discordNotificationsEnabled ? 'bg-brand' : 'bg-gray-300 dark:bg-gray-600'} disabled:opacity-50"
				role="switch"
				aria-checked={discordNotificationsEnabled}
				data-testid="discord-toggle"
				aria-label="Toggle Discord notifications"
			>
				<span
					class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {discordNotificationsEnabled ? 'translate-x-6' : 'translate-x-1'}"
				></span>
			</button>
		</div>

		<div class="flex gap-2">
			<input
				type="url"
				placeholder={discordWebhookConfigured ? 'Webhook configured — paste a new URL to replace' : 'https://discord.com/api/webhooks/...'}
				value={discordWebhookUrl}
				onchange={handleDiscordWebhookChange}
				disabled={discordLoading}
				class="flex-1 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 disabled:opacity-50"
				data-testid="discord-webhook-url"
			/>
			<button
				onclick={handleTestDiscordWebhook}
				disabled={discordTestLoading || !discordWebhookConfigured}
				class="px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
				data-testid="discord-test-button"
			>
				{discordTestLoading ? 'Sending...' : 'Test'}
			</button>
		</div>

		<p class="text-xs text-gray-400 dark:text-gray-500">
			Get a webhook URL from your Discord server: Server Settings &rarr; Integrations &rarr; Webhooks
		</p>
	</div>

	{#if pageData.settings?.telegramAvailable}
		<PushChannelCard
			channel="telegram"
			title="Telegram Notifications"
			description="Send price alerts to a Telegram chat"
			recipientLabel="Telegram chat id"
			placeholder="Chat id, e.g. 123456789"
			configured={pageData.settings.telegramConfigured}
			enabled={pageData.settings.telegramNotificationsEnabled}
		>
			{#snippet help()}
				{#if pageData.settings?.telegramBotUsername}
					Start a chat with
					<a
						href="https://t.me/{pageData.settings.telegramBotUsername}"
						target="_blank"
						rel="noopener noreferrer external"
						class="text-brand dark:text-brand-muted hover:underline">@{pageData.settings.telegramBotUsername}</a
					>
					first — it can only message chats that have messaged it.
				{:else}
					Start a chat with this server's bot first — it can only message chats that have messaged it.
				{/if}
				Your chat id is the number @userinfobot replies with.
			{/snippet}
		</PushChannelCard>
	{/if}

	{#if pageData.settings?.pushoverAvailable}
		<PushChannelCard
			channel="pushover"
			title="Pushover Notifications"
			description="Send price alerts to your devices through Pushover"
			recipientLabel="Pushover user key"
			placeholder="30-character user key"
			configured={pageData.settings.pushoverConfigured}
			enabled={pageData.settings.pushoverNotificationsEnabled}
		>
			{#snippet help()}
				Your user key is on the Pushover dashboard after you sign in at pushover.net.
			{/snippet}
		</PushChannelCard>
	{/if}

	<WebhookList
		webhooks={webhookTargets}
		testingId={webhookTestingId}
		deletingId={deletingWebhookId}
		onAdd={() => { editingWebhook = undefined; webhookModalOpen = true; }}
		onEdit={(webhook) => { editingWebhook = webhook; webhookModalOpen = true; }}
		onTest={handleTestWebhook}
		onDelete={handleDeleteWebhook}
	/>
</div>

<WebhookModal
	isOpen={webhookModalOpen}
	target={editingWebhook}
	onClose={() => { webhookModalOpen = false; editingWebhook = undefined; }}
	onSave={handleSaveWebhook}
/>

<ConfirmModal
	isOpen={webhookDeleteConfirmOpen}
	title="Delete Webhook"
	message="Are you sure you want to delete this webhook? This action cannot be undone."
	onConfirm={handleConfirmDeleteWebhook}
	onCancel={() => { webhookDeleteConfirmOpen = false; deletingWebhookId = undefined; }}
	loading={webhookDeleteLoading}
/>
