import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import PushChannelCard from './PushChannelCard.svelte';
import { api } from '$lib/api/client';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: { updateSettings: vi.fn(), testPushChannel: vi.fn() }
	};
});

const base = {
	channel: 'telegram' as const,
	title: 'Telegram Notifications',
	description: 'Send price alerts to a Telegram chat',
	recipientLabel: 'Telegram chat id',
	placeholder: '123456789',
	configured: false,
	enabled: false
};

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.updateSettings).mockResolvedValue({} as never);
});

describe('PushChannelCard', () => {
	it('should save the recipient under the channel field and mark it configured', async () => {
		render(PushChannelCard, { props: base });
		const input = screen.getByLabelText('Telegram chat id');
		await fireEvent.change(input, { target: { value: ' 42 ' } });

		expect(api.updateSettings).toHaveBeenCalledWith({ telegramChatId: '42' });
		await waitFor(() => expect(screen.getByTestId('telegram-test-button')).not.toBeDisabled());
		expect(input).toHaveValue('');
	});

	it('should save a Pushover key under pushoverUserKey', async () => {
		render(PushChannelCard, {
			props: { ...base, channel: 'pushover', recipientLabel: 'Pushover user key' }
		});
		await fireEvent.change(screen.getByLabelText('Pushover user key'), {
			target: { value: 'uQiRzpo4DXghDmr9QzzfQu27cmVRsG' }
		});
		expect(api.updateSettings).toHaveBeenCalledWith({
			pushoverUserKey: 'uQiRzpo4DXghDmr9QzzfQu27cmVRsG'
		});
	});

	it('should save an ntfy topic URL under ntfyTopicUrl and toggle ntfy by name', async () => {
		render(PushChannelCard, {
			props: { ...base, channel: 'ntfy', recipientLabel: 'ntfy topic URL', configured: true }
		});
		await fireEvent.change(screen.getByLabelText('ntfy topic URL'), {
			target: { value: 'https://ntfy.sh/ophi-alerts' }
		});
		expect(api.updateSettings).toHaveBeenCalledWith({ ntfyTopicUrl: 'https://ntfy.sh/ophi-alerts' });

		await fireEvent.click(screen.getByRole('switch', { name: 'Toggle ntfy notifications' }));
		expect(api.updateSettings).toHaveBeenCalledWith({ ntfyNotificationsEnabled: true });
	});

	it('should say a recipient is saved without showing it', () => {
		render(PushChannelCard, { props: { ...base, configured: true } });
		expect(screen.getByLabelText('Telegram chat id')).toHaveAttribute(
			'placeholder',
			expect.stringContaining('saved')
		);
	});

	it('should toggle the channel via the switch', async () => {
		render(PushChannelCard, { props: { ...base, configured: true } });
		await fireEvent.click(screen.getByRole('switch', { name: 'Toggle Telegram notifications' }));
		expect(api.updateSettings).toHaveBeenCalledWith({ telegramNotificationsEnabled: true });
		await waitFor(() =>
			expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true')
		);
	});

	it('should disable Test until a recipient is saved', () => {
		render(PushChannelCard, { props: base });
		expect(screen.getByTestId('telegram-test-button')).toBeDisabled();
	});

	it('should surface the server error from a failed test send', async () => {
		const spy = vi.spyOn(toast, 'error');
		vi.mocked(api.testPushChannel).mockResolvedValue({
			success: false,
			error: 'Telegram returned an error: Forbidden'
		});
		render(PushChannelCard, { props: { ...base, configured: true } });
		await fireEvent.click(screen.getByTestId('telegram-test-button'));
		expect(api.testPushChannel).toHaveBeenCalledWith('telegram');
		await waitFor(() => expect(spy).toHaveBeenCalledWith('Telegram returned an error: Forbidden'));
	});

	it('should show the field message from a validation ApiError, not the generic summary', async () => {
		const { ApiError } = await import('$lib/api/client');
		const spy = vi.spyOn(toast, 'error');
		vi.mocked(api.updateSettings).mockRejectedValue(
			new ApiError('VALIDATION_ERROR', 'One or more validation errors occurred', {
				PushoverUserKey: ['Pushover user key must be 30 letters and digits']
			})
		);
		render(PushChannelCard, { props: { ...base, channel: 'pushover', recipientLabel: 'Pushover user key' } });
		await fireEvent.change(screen.getByLabelText('Pushover user key'), { target: { value: 'x' } });
		await waitFor(() =>
			expect(spy).toHaveBeenCalledWith('Pushover user key must be 30 letters and digits')
		);
	});

	it('should show the server validation message when saving fails', async () => {
		const spy = vi.spyOn(toast, 'error');
		vi.mocked(api.updateSettings).mockRejectedValue(new Error('Telegram chat id must be a number'));
		render(PushChannelCard, { props: base });
		await fireEvent.change(screen.getByLabelText('Telegram chat id'), { target: { value: 'abc' } });
		await waitFor(() => expect(spy).toHaveBeenCalledWith('Telegram chat id must be a number'));
	});
});
