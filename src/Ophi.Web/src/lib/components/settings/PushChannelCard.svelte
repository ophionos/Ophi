<script lang="ts">
	import type { Snippet } from 'svelte';
	import { api, ApiError, type PushChannel, type UpdateSettingsRequest } from '$lib/api/client';
	import { toast } from '$lib/stores/toast.svelte';

	interface Props {
		channel: PushChannel;
		title: string;
		description: string;
		/** Accessible label of the recipient input (chat id / user key / topic URL). */
		recipientLabel: string;
		placeholder: string;
		/** A recipient is saved. The value itself is never sent back by the API. */
		configured: boolean;
		enabled: boolean;
		/** How to find the recipient value. */
		help?: Snippet;
	}

	let {
		channel,
		title,
		description,
		recipientLabel,
		placeholder,
		configured: initialConfigured,
		enabled: initialEnabled,
		help
	}: Props = $props();

	// Writable deriveds: seeded from props, then updated locally after each save.
	let configured = $derived(initialConfigured);
	let enabled = $derived(initialEnabled);
	let saving = $state(false);
	let testing = $state(false);

	const channelNames: Record<PushChannel, string> = {
		telegram: 'Telegram',
		pushover: 'Pushover',
		ntfy: 'ntfy'
	};
	const channelName = $derived(channelNames[channel]);
	const inputId = $derived(`${channel}-recipient`);

	function recipientPatch(value: string): UpdateSettingsRequest {
		switch (channel) {
			case 'telegram':
				return { telegramChatId: value };
			case 'pushover':
				return { pushoverUserKey: value };
			case 'ntfy':
				return { ntfyTopicUrl: value };
		}
	}

	function enabledPatch(value: boolean): UpdateSettingsRequest {
		switch (channel) {
			case 'telegram':
				return { telegramNotificationsEnabled: value };
			case 'pushover':
				return { pushoverNotificationsEnabled: value };
			case 'ntfy':
				return { ntfyNotificationsEnabled: value };
		}
	}

	// A validation failure's useful text is the field message, not the generic summary.
	function errorMessage(err: unknown): string {
		if (err instanceof ApiError) {
			const field = Object.values(err.details).flat()[0];
			if (field) return field;
		}
		return err instanceof Error ? err.message : 'Failed to update settings';
	}

	async function handleRecipientChange(e: Event) {
		const input = e.currentTarget as HTMLInputElement;
		const value = input.value.trim();
		saving = true;
		try {
			await api.updateSettings(recipientPatch(value));
			configured = !!value;
			input.value = '';
			toast.success(value ? `${recipientLabel} saved` : `${recipientLabel} cleared`);
		} catch (err) {
			toast.error(errorMessage(err));
		} finally {
			saving = false;
		}
	}

	async function handleToggle() {
		const next = !enabled;
		saving = true;
		try {
			await api.updateSettings(enabledPatch(next));
			enabled = next;
			toast.success(`${channelName} notifications ${next ? 'enabled' : 'disabled'}`);
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to update settings');
		} finally {
			saving = false;
		}
	}

	async function handleTest() {
		testing = true;
		try {
			const result = await api.testPushChannel(channel);
			if (result.success) toast.success(`Test message sent to ${channelName}!`);
			else toast.error(result.error ?? 'Failed to send test message');
		} catch (err) {
			toast.error(err instanceof Error ? err.message : 'Failed to send test message');
		} finally {
			testing = false;
		}
	}
</script>

<div
	class="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 space-y-3"
	data-testid="{channel}-card"
>
	<div class="flex items-center justify-between gap-3">
		<div class="min-w-0">
			<p class="text-sm font-medium text-gray-900 dark:text-white">{title}</p>
			<p class="text-xs text-gray-500 dark:text-gray-400">{description}</p>
		</div>
		<button
			onclick={handleToggle}
			disabled={saving}
			class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors {enabled
				? 'bg-brand'
				: 'bg-gray-300 dark:bg-gray-600'} disabled:opacity-50"
			role="switch"
			aria-checked={enabled}
			aria-label="Toggle {channelName} notifications"
		>
			<span
				class="inline-block h-4 w-4 transform rounded-full bg-white transition-transform {enabled
					? 'translate-x-6'
					: 'translate-x-1'}"
			></span>
		</button>
	</div>

	<div class="flex gap-2">
		<label for={inputId} class="sr-only">{recipientLabel}</label>
		<input
			id={inputId}
			type="text"
			autocomplete="off"
			spellcheck="false"
			placeholder={configured ? `${recipientLabel} saved — enter a new one to replace` : placeholder}
			onchange={handleRecipientChange}
			disabled={saving}
			class="flex-1 min-w-0 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-gray-500 disabled:opacity-50"
		/>
		<button
			onclick={handleTest}
			disabled={testing || !configured}
			class="px-3 py-1.5 text-sm font-medium text-white bg-brand hover:bg-brand-hover rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
			data-testid="{channel}-test-button"
		>
			{testing ? 'Sending...' : 'Test'}
		</button>
	</div>

	{#if help}
		<div class="text-xs text-gray-400 dark:text-gray-500">{@render help()}</div>
	{/if}
</div>
